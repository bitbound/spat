using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using Spat.Libraries.Core.Audio;

namespace Spat.Libraries.Native.Windows;

/// <summary>
/// Lists the active capture endpoints WASAPI exposes, flagging the one Windows records by default.
/// </summary>
public sealed class WasapiDeviceEnumerator(ILogger<WasapiDeviceEnumerator> logger) : IAudioCaptureDeviceEnumerator
{
    private readonly ILogger<WasapiDeviceEnumerator> _logger = logger;

    public Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();

            string? defaultId = null;

            try
            {
                using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                defaultId = endpoint.ID;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Windows reported no default capture endpoint.");
            }

            var devices = new List<AudioDevice>();

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                {
                    devices.Add(new AudioDevice(device.ID, device.FriendlyName, device.ID == defaultId));
                }
            }

            return Task.FromResult<IReadOnlyList<AudioDevice>>(devices);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Could not enumerate audio capture devices: {exception.Message}", exception);
        }
    }
}
