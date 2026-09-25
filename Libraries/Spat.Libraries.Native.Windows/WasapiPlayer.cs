using System.IO;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Spat.Libraries.Core.Audio;

namespace Spat.Libraries.Native.Windows;

/// <summary>
/// Plays back the 16-bit PCM WAV recordings Spat stores, through the default WASAPI render device.
/// </summary>
public sealed class WasapiPlayer : IAudioPlayer
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    private readonly ILogger<WasapiPlayer> _logger;

    public WasapiPlayer(ILogger<WasapiPlayer> logger)
    {
        _logger = logger;
    }

    public async Task PlayAsync(byte[] wavBytes, CancellationToken cancellationToken = default)
    {
        var clip = WavFile.TryRead(wavBytes)
            ?? throw new InvalidOperationException("Only the 16-bit PCM WAV recordings Spat stores can be played.");

        var format = new WaveFormat(clip.SampleRate, clip.BitsPerSample, clip.ChannelCount);
        var duration = TimeSpan.FromSeconds((double)clip.PcmBytes.Length / format.AverageBytesPerSecond);

        using var outDevice = ResolveRenderDevice();
        using var output = new WasapiOut(outDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 200);
        using var source = new RawSourceWaveStream(new MemoryStream(clip.PcmBytes, writable: false), format);

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        output.PlaybackStopped += (_, e) =>
        {
            if (e.Exception is not null)
            {
                stopped.TrySetException(new InvalidOperationException($"Playback failed: {e.Exception.Message}", e.Exception));
                return;
            }

            stopped.TrySetResult();
        };

        using var cancellation = cancellationToken.Register(() => SafeStop(output));

        output.Init(source);
        output.Play();

        var playback = await Task.WhenAny(stopped.Task, Task.Delay(duration + Grace)).ConfigureAwait(false);

        if (playback != stopped.Task)
        {
            SafeStop(output);
            _logger.LogWarning("Playback of a {Duration:n0} ms clip was cut off by the watchdog.", duration.TotalMilliseconds);
            return;
        }

        await stopped.Task.ConfigureAwait(false);
    }

    private static MMDevice ResolveRenderDevice()
    {
        using var enumerator = new MMDeviceEnumerator();

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    private static void SafeStop(IWavePlayer player)
    {
        try
        {
            player.Stop();
        }
        catch
        {
            // Stop races natural completion or a torn-down device.
        }
    }
}
