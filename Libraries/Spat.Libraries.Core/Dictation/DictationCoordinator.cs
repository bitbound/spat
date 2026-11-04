using Microsoft.Extensions.Logging;
using Spat.Libraries.Core.Audio;
using Spat.Libraries.Core.CustomDictionary;
using Spat.Libraries.Core.History;
using Spat.Libraries.Core.Input;
using Spat.Libraries.Core.Prompts;
using Spat.Libraries.Core.Settings;
using Spat.Libraries.Core.Speech;
using Spat.Libraries.Core.SpeechCommands;

namespace Spat.Libraries.Core.Dictation;

/// <summary>
/// Runs one dictation end to end: capture, transcribe, optional post-process, history, then typing.
/// </summary>
public sealed class DictationCoordinator : IDictationCoordinator
{
    private const float MinimumActiveSampleRatio = 0.01f;

    /// <summary>
    /// How much of a post-processing failure reason reaches a listener. Long enough for an endpoint
    /// and its status, short enough to sit on one line of the status indicator.
    /// </summary>
    private const int MaxReasonLength = 160;

    private readonly ITextInputInjector _injector;
    private readonly ILogger<DictationCoordinator> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly IAudioRecorder _recorder;
    private readonly IRecordingStore _recordings;
    private readonly ISettingsService _settings;
    private readonly IPromptService _prompts;
    private readonly ICustomDictionaryService _dictionary;
    private readonly ISpeechCommandService _speechCommands;
    private readonly IHistoryService _history;
    private readonly ISpeechToTextClient _speechToText;
    private readonly ITextGenerationClient _textGeneration;

    private CancellationTokenSource? _endOfRecording;
    private Task? _activeRun;

    public DictationCoordinator(
        IAudioRecorder recorder,
        IRecordingStore recordings,
        ISpeechToTextClient speechToText,
        ITextGenerationClient textGeneration,
        IPromptService prompts,
        ICustomDictionaryService dictionary,
        ISpeechCommandService speechCommands,
        IHistoryService history,
        ITextInputInjector injector,
        ISettingsService settings,
        TimeProvider timeProvider,
        ILogger<DictationCoordinator> logger)
    {
        _recorder = recorder;
        _recordings = recordings;
        _speechToText = speechToText;
        _textGeneration = textGeneration;
        _prompts = prompts;
        _dictionary = dictionary;
        _speechCommands = speechCommands;
        _history = history;
        _injector = injector;
        _settings = settings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public DictationState State { get; private set; } = DictationState.Idle;

    public string? LastError { get; private set; }

    public bool IsListening => State == DictationState.Listening;

    public event EventHandler? StateChanged;

    public event EventHandler<DictationLimitReachedEventArgs>? RecordingLimitReached;

    public event EventHandler<PostProcessingFailedEventArgs>? PostProcessingFailed;

    public async Task ToggleAsync(CancellationToken cancellationToken = default)
    {
        if (IsListening)
        {
            await StopAsync(cancellationToken);
            return;
        }

        if (CanStart(State))
        {
            await StartAsync(cancellationToken);
        }
    }

    // Error has to be restartable or a single failed take silences the hotkey until the app is restarted:
    // nothing else ever moves the state back off Error.
    private static bool CanStart(DictationState state)
    {
        return state is DictationState.Idle or DictationState.Error;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!CanStart(State))
            {
                return Task.CompletedTask;
            }

            if (!_settings.Current.SpeechToText.IsConfigured)
            {
                var speech = _settings.Current.SpeechToText;
                Fail(speech.ModelSelectionEnabled
                    ? "Configure the speech-to-text endpoint and model in Settings first."
                    : "Configure the speech-to-text endpoint in Settings first.");
                return Task.CompletedTask;
            }

            LastError = null;
            _endOfRecording = new CancellationTokenSource();
            SetState(DictationState.Listening);

            var endOfRecording = _endOfRecording;
            var maxDuration = TimeSpan.FromSeconds(Math.Clamp(_settings.Current.MaximumRecordingSeconds, 1, 3600));

            _activeRun = Task.Run(() => RunAsync(endOfRecording, maxDuration, cancellationToken));
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? run;

        lock (_gate)
        {
            if (State != DictationState.Listening)
            {
                return;
            }

            run = _activeRun;
        }

        _endOfRecording?.Cancel();

        if (run is not null)
        {
            await run.ConfigureAwait(false);
        }
    }

    private async Task RunAsync(CancellationTokenSource endOfRecording, TimeSpan maxDuration, CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        var pcm = PcmAudio.Empty;
        var reachedLimit = false;

        try
        {
            using (endOfRecording)
            {
                // The limit gets a source of its own, linked to the one StopAsync cancels. Sharing a
                // single source would leave no way to tell the two reasons apart after the fact.
                using var limit = new CancellationTokenSource();
                using var limitTimer = _timeProvider.CreateTimer(static state => CancelLimit((CancellationTokenSource)state!), limit, maxDuration, Timeout.InfiniteTimeSpan);
                using var ended = CancellationTokenSource.CreateLinkedTokenSource(endOfRecording.Token, limit.Token);

                pcm = await _recorder
                    .RecordAsync(_settings.Current.InputDeviceId, ended.Token, maxDuration, cancellationToken)
                    .ConfigureAwait(false);

                // A stop that lands in the same instant as the limit reads as the user's stop.
                reachedLimit = limit.IsCancellationRequested
                    && !endOfRecording.IsCancellationRequested
                    && !cancellationToken.IsCancellationRequested;
            }

            lock (_gate)
            {
                _endOfRecording = null;
                _activeRun = null;
            }

            if (reachedLimit)
            {
                ReportLimitReached(maxDuration);
            }

            var silenceThreshold = Math.Clamp(
                _settings.Current.SilenceRmsThreshold,
                0.0001f,
                0.1f);

            var audioStats = GetAudioStats(pcm, silenceThreshold);
            _logger.LogDebug(
                "Captured {Frames} frames with peak {Peak:0.0000}, RMS {Rms:0.0000}, and active ratio {ActiveRatio:0.0000}.",
                pcm.FrameCount,
                audioStats.Peak,
                audioStats.Rms,
                audioStats.ActiveRatio);

            if (pcm.FrameCount == 0
                || audioStats.Rms <= silenceThreshold
                || audioStats.ActiveRatio < MinimumActiveSampleRatio)
            {
                SetState(DictationState.Idle);
                return;
            }

            SetState(DictationState.Transcribing);

            var wav = PcmProcessor.ToWav(pcm);
            var initialPrompt = _settings.Current.Dictionary.HintFirstPassEnabled
                ? _dictionary.BuildHintPrompt()
                : null;
            var transcription = (await _speechToText
                .TranscribeAsync(wav, initialPrompt, cancellationToken)
                .ConfigureAwait(false)).Trim();

            // The dictionary replacement runs before history is written, so the entry shows what got
            // typed. Transcription keeps what the model actually heard.
            var finalText = _dictionary.Replace(transcription);
            var promptTitle = (string?)null;

            if (string.IsNullOrWhiteSpace(finalText))
            {
                SetState(DictationState.Idle);
                return;
            }

            var durationMs = (long)_timeProvider.GetElapsedTime(started, _timeProvider.GetTimestamp()).TotalMilliseconds;
            var entryId = Guid.NewGuid();
            var audioFileName = await SaveRecordingAsync(entryId, wav, cancellationToken).ConfigureAwait(false);
            var historyEntry = new HistoryEntry
            {
                Id = entryId,
                Text = finalText,
                ModelId = _settings.Current.SpeechToText.ModelSelectionEnabled
                    ? _settings.Current.SpeechToText.ModelId
                    : null,
                AudioFileName = audioFileName,
                DurationMs = durationMs,
            };

            if (!string.Equals(finalText, transcription, StringComparison.Ordinal))
            {
                historyEntry.Transcription = transcription;
            }

            try
            {
                await _history.AddAsync(historyEntry, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The audio is only reachable through the row, so it goes back out with the row.
                _recordings.Delete(audioFileName);

                throw;
            }

            if (_settings.Current.PostProcessing.IsConfigured && !string.IsNullOrWhiteSpace(finalText))
            {
                SetState(DictationState.PostProcessing);

                var prompt = _prompts.GetSelected();
                promptTitle = prompt.Title;
                var dictionaryBlock = _settings.Current.Dictionary.IncludeInPostProcessingEnabled
                    ? _dictionary.BuildCorrectionBlock()
                    : null;

                var speechCommandsBlock = _speechCommands.BuildInstructionBlock();

                // The model cleans up the deterministic result rather than the raw transcription, so a
                // dictionary term that PhraseReplacer already fixed survives post-processing.
                var rendered = PromptRenderer.Render(prompt.Instructions, finalText, dictionaryBlock, speechCommandsBlock);

                string? processed;

                try
                {
                    processed = (await _textGeneration
                        .CompleteAsync(rendered.SystemPrompt, rendered.UserPrompt, cancellationToken)
                        .ConfigureAwait(false)).Trim();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The words are already on hand, so a cleanup pass that cannot be reached, timed
                    // out, or answered badly must not take them along. The raw transcription is typed
                    // and the reason is reported instead of the whole dictation failing.
                    if (ex is AiEndpointException or HttpRequestException)
                    {
                        _logger.LogWarning(ex, "Post-processing failed; using the raw transcription.");
                    }
                    else
                    {
                        // Not an endpoint problem but a fault in our own code, so it must stay visible
                        // in error telemetry even though the take itself is still rescued.
                        _logger.LogError(ex, "Unexpected post-processing fault; using the raw transcription.");
                    }

                    ReportPostProcessingFailed(ex.Message);
                    processed = null;
                }

                if (!string.IsNullOrWhiteSpace(processed))
                {
                    finalText = processed;
                    historyEntry.Text = processed;
                    historyEntry.Transcription = transcription;
                    historyEntry.PromptTitle = promptTitle;
                    historyEntry.DurationMs = (long)_timeProvider.GetElapsedTime(started, _timeProvider.GetTimestamp()).TotalMilliseconds;
                    await _history.UpdateAsync(historyEntry, cancellationToken).ConfigureAwait(false);
                }
            }

            if (string.IsNullOrWhiteSpace(finalText))
            {
                SetState(DictationState.Idle);
                return;
            }

            SetState(DictationState.Injecting);

            await _injector.TypeAsync(finalText, cancellationToken).ConfigureAwait(false);

            SetState(DictationState.Idle);
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                _endOfRecording = null;
                _activeRun = null;
            }

            SetState(DictationState.Idle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dictation failed.");

            lock (_gate)
            {
                _endOfRecording = null;
                _activeRun = null;
            }

            Fail(ex.Message);
        }
    }

    // The WAV is already rendered for the endpoint, so keeping it costs a copy rather than another
    // capture. A failure to write it is not worth losing a dictation that transcribed fine.
    private async Task<string?> SaveRecordingAsync(Guid entryId, byte[] wav, CancellationToken cancellationToken)
    {
        if (!_settings.Current.KeepRecordings)
        {
            return null;
        }

        try
        {
            return await _recordings.SaveAsync(entryId, wav, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not save the recording for history.");

            return null;
        }
    }

    private void Fail(string message)
    {
        LastError = message;
        SetState(DictationState.Error);
    }

    private static (float Peak, float Rms, float ActiveRatio) GetAudioStats(PcmAudio audio, float silenceThreshold)
    {
        var peak = 0f;
        var sumOfSquares = 0d;
        var finiteSamples = 0;
        var activeSamples = 0;

        foreach (var sample in audio.InterleavedSamples)
        {
            if (!float.IsFinite(sample))
            {
                continue;
            }

            var magnitude = MathF.Abs(sample);
            peak = MathF.Max(peak, magnitude);
            sumOfSquares += sample * sample;
            finiteSamples++;

            if (magnitude > silenceThreshold)
            {
                activeSamples++;
            }
        }

        return finiteSamples == 0
            ? (0, 0, 0)
            : (peak, (float)Math.Sqrt(sumOfSquares / finiteSamples), (float)activeSamples / finiteSamples);
    }

    // The timer can fire while a finished run is being torn down, so the source may already be gone.
    // That race is the normal outcome at the end of a take, not a fault.
    private static void CancelLimit(CancellationTokenSource limit)
    {
        try
        {
            limit.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    // The take survives, so this is a notice about what was captured, not a failure. How it is shown
    // is left to the listener.
    private void ReportLimitReached(TimeSpan limit)
    {
        _logger.LogWarning("The recording hit its {Seconds:0} second limit and stopped.", limit.TotalSeconds);

        RecordingLimitReached?.Invoke(this, new DictationLimitReachedEventArgs(limit));
    }

    // As with the limit, the dictation itself succeeded and only part of it was lost, so the reason
    // travels to the listener rather than becoming LastError, which would read as a failed take.
    private void ReportPostProcessingFailed(string reason)
    {
        // The reason is shown on the floating pill, which grows to fit it, and an endpoint detail can
        // be a whole response body. The log keeps the exception in full.
        var summary = string.Join(' ', reason.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (summary.Length > MaxReasonLength)
        {
            var cut = MaxReasonLength;

            // The cap counts UTF-16 code units, so back off a lead surrogate the cut would strand —
            // HarfBuzz renders an orphaned lead as a visible replacement glyph on the pill.
            if (char.IsHighSurrogate(summary[cut - 1]))
            {
                cut--;
            }

            summary = string.Concat(summary.AsSpan(0, cut).TrimEnd(), "...");
        }

        PostProcessingFailed?.Invoke(this, new PostProcessingFailedEventArgs(summary));
    }

    private void SetState(DictationState state)
    {
        lock (_gate)
        {
            State = state;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
