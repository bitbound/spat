using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Spat.Libraries.Core.Audio;

namespace Spat.Libraries.Native.Windows;

/// <summary>
/// Captures the microphone through WASAPI shared mode and returns normalized float PCM resampled to
/// 16 kHz mono, the format the speech endpoint and level meter expect.
/// </summary>
public sealed class WasapiRecorder : IAudioRecorder, IAudioLevelMeter
{
    /// <summary>
    /// The rate and channel count every recording is delivered at, whatever the device runs at natively.
    /// </summary>
    internal const int TargetSampleRate = 16_000;

    /// <summary>
    /// Bounds on top of the requested maximum used when the audio server stops delivering anything.
    /// </summary>
    private static readonly TimeSpan ServerGrace = TimeSpan.FromSeconds(30);

    private readonly ILogger<WasapiRecorder> _logger;

    private float _level;

    public WasapiRecorder(ILogger<WasapiRecorder> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Peak amplitude of the most recent frame, normalized from 0 to 1. Only advances while recording.
    /// </summary>
    public float Level => Volatile.Read(ref _level);

    public event EventHandler<float>? LevelChanged;

    public async Task<PcmAudio> RecordAsync(
        string? deviceId,
        CancellationToken endOfRecording,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var device = ResolveDevice(deviceId);

        // Shared mode with a 200 ms buffer; the device mix format is resampled by us below.
        using var capture = new WasapiCapture(device, false, 200);

        var chunks = new List<float[]>();
        var gate = new TaskCompletionSource<PcmAudio>(TaskCreationOptions.RunContinuationsAsynchronously);
        var priming = true;
        var sampleRate = capture.WaveFormat.SampleRate;
        var channels = Math.Max(1, capture.WaveFormat.Channels);

        capture.DataAvailable += (_, e) =>
        {
            try
            {
                var samples = ToFloats(e.Buffer.AsSpan(0, e.BytesRecorded), capture.WaveFormat);

                if (samples.Length == 0)
                {
                    return;
                }

                // The first buffer repeats what the device ring already held before this capture began.
                if (priming)
                {
                    priming = false;
                    return;
                }

                ReportLevel(ComputePeak(samples.AsSpan()));

                lock (chunks)
                {
                    chunks.Add(samples);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "A captured audio frame could not be converted.");
            }
        };

        capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null)
            {
                gate.TrySetException(new InvalidOperationException(
                    $"The audio capture failed: {e.Exception.Message}", e.Exception));
                return;
            }

            lock (chunks)
            {
                var interleaved = Concatenate(chunks);
                var mono = Downmix(interleaved, channels);
                var resampled = Resample(mono, sampleRate, TargetSampleRate);

                gate.TrySetResult(new PcmAudio(resampled, TargetSampleRate, 1));
            }
        };

        using var endRegistration = endOfRecording.Register(() => SafeStop(capture));
        using var callerRegistration = cancellationToken.Register(() => SafeStop(capture));

        try
        {
            capture.StartRecording();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Could not start audio capture: {exception.Message}", exception);
        }

        using var serverWatchdog = new CancellationTokenSource(timeout + ServerGrace);
        var completion = await Task.WhenAny(
            gate.Task,
            Task.Delay(Timeout.InfiniteTimeSpan, serverWatchdog.Token)).ConfigureAwait(false);

        if (completion != gate.Task)
        {
            SafeStop(capture);
            throw new InvalidOperationException("The Windows audio server did not deliver the recording in time.");
        }

        var recording = await gate.Task.ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        return recording;
    }

    /// <summary>
    /// Largest sample magnitude in a frame of float bytes; NaN samples are ignored and the result is
    /// clamped to full scale.
    /// </summary>
    internal static float ComputePeak(byte[] frame)
    {
        var peak = 0f;

        for (var offset = 0; offset + sizeof(float) <= frame.Length; offset += sizeof(float))
        {
            var sample = Math.Abs(BitConverter.ToSingle(frame, offset));

            if (float.IsNaN(sample))
            {
                continue;
            }

            peak = Math.Max(peak, sample);
        }

        return Math.Clamp(peak, 0f, 1f);
    }

    internal static float ComputePeak(ReadOnlySpan<float> samples)
    {
        var peak = 0f;

        foreach (var sample in samples)
        {
            var magnitude = Math.Abs(sample);

            if (float.IsNaN(magnitude))
            {
                continue;
            }

            peak = Math.Max(peak, magnitude);
        }

        return Math.Clamp(peak, 0f, 1f);
    }

    private static MMDevice ResolveDevice(string? deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();

        if (string.IsNullOrEmpty(deviceId))
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        }

        try
        {
            return enumerator.GetDevice(deviceId);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"The selected capture device is no longer available: {exception.Message}", exception);
        }
    }

    private static float[] ToFloats(ReadOnlySpan<byte> buffer, WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            var floats = new float[buffer.Length / sizeof(float)];
            buffer.CopyTo(MemoryMarshal.AsBytes(floats.AsSpan()));

            return floats;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            var floats = new float[buffer.Length / sizeof(short)];

            for (var i = 0; i < floats.Length; i++)
            {
                var raw = (short)(buffer[(i * 2) + 1] << 8 | buffer[i * 2]);

                floats[i] = raw / 32768f;
            }

            return floats;
        }

        throw new InvalidOperationException($"Unsupported capture format {format}.");
    }

    private static float[] Concatenate(List<float[]> chunks)
    {
        var total = 0;
        foreach (var chunk in chunks)
        {
            total += chunk.Length;
        }

        var all = new float[total];
        var offset = 0;

        foreach (var chunk in chunks)
        {
            chunk.CopyTo(all, offset);
            offset += chunk.Length;
        }

        return all;
    }

    private static float[] Downmix(float[] interleaved, int channels)
    {
        if (channels == 1)
        {
            return interleaved;
        }

        var frames = interleaved.Length / channels;
        var mono = new float[frames];

        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;

            for (var channel = 0; channel < channels; channel++)
            {
                sum += interleaved[(frame * channels) + channel];
            }

            mono[frame] = sum / channels;
        }

        return mono;
    }

    /// <summary>
    /// Linear interpolation resampling. Good enough for speech and dependency free.
    /// </summary>
    internal static float[] Resample(float[] input, int sourceRate, int targetRate)
    {
        if (input.Length == 0 || sourceRate == targetRate)
        {
            return input;
        }

        var targetLength = (int)((long)input.Length * targetRate / sourceRate);
        var output = new float[targetLength];
        var step = (double)sourceRate / targetRate;

        for (var i = 0; i < targetLength; i++)
        {
            var position = i * step;
            var left = (int)position;
            var right = Math.Min(left + 1, input.Length - 1);
            var fraction = (float)(position - left);

            output[i] = (input[left] * (1 - fraction)) + (input[right] * fraction);
        }

        return output;
    }

    private void ReportLevel(float peak)
    {
        Volatile.Write(ref _level, peak);
        LevelChanged?.Invoke(this, peak);
    }

    private static void SafeStop(IWaveIn capture)
    {
        try
        {
            capture.StopRecording();
        }
        catch
        {
            // Stop races the device being torn down; RecordingStopped or the watchdog resolves the wait.
        }
    }
}
