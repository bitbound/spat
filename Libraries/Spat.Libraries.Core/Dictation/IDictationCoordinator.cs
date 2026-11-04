namespace Spat.Libraries.Core.Dictation;

public interface IDictationCoordinator
{
    DictationState State { get; }

    string? LastError { get; }

    bool IsListening { get; }

    event EventHandler? StateChanged;

    /// <summary>
    /// Raised when a recording is cut short by the configured maximum length. It fires before the
    /// take is transcribed, so a listener sees it while the rest of the run is still going.
    /// </summary>
    event EventHandler<DictationLimitReachedEventArgs>? RecordingLimitReached;

    /// <summary>
    /// Raised when post-processing is enabled but the text model could not be used, for any reason.
    /// The raw transcription is typed either way, so this reports what was skipped rather than a
    /// failed dictation. It fires while the run is still going, before the text is typed.
    /// </summary>
    event EventHandler<PostProcessingFailedEventArgs>? PostProcessingFailed;

    Task ToggleAsync(CancellationToken cancellationToken = default);

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
