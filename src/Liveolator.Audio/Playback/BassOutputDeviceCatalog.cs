using System.Globalization;
using Liveolator.Core.Audio;
using ManagedBass;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Audio.Playback;

/// <summary>
/// BASS-backed <see cref="IAudioOutputDeviceCatalog"/> (doc 01): enumerates the sound-card output
/// endpoints for the Settings device picker. Mirrors <see cref="Capture.BassCaptureEngine"/> on the
/// output side — enumeration degrades to an empty list when native BASS is absent (CI / a dev box
/// without the per-platform binaries) so it never crashes the UI. The device <see cref="AudioOutputDevice.Id"/>
/// is the BASS device index as a string, which the playback init resolves back when opening output.
/// </summary>
public sealed class BassOutputDeviceCatalog : IAudioOutputDeviceCatalog
{
    private readonly ILogger<BassOutputDeviceCatalog> _logger;

    public BassOutputDeviceCatalog(ILoggerFactory? loggerFactory = null)
        => _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<BassOutputDeviceCatalog>();

    public IReadOnlyList<AudioOutputDevice> EnumerateOutputDevices()
    {
        var devices = new List<AudioOutputDevice>();
        try
        {
            // BASS device 0 is the "No sound" device — skip it; real outputs start at index 1.
            for (int i = 1; Bass.GetDeviceInfo(i, out DeviceInfo info); i++)
            {
                if (!info.IsEnabled)
                    continue;

                devices.Add(new AudioOutputDevice(
                    Id: i.ToString(CultureInfo.InvariantCulture),
                    Name: info.Name ?? $"Output device {i}",
                    IsDefault: info.IsDefault));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            // Native bass absent: surface no devices rather than crashing (global standards #16/#26).
            _logger.LogWarning(ex, "Output device enumeration unavailable: native BASS not loaded.");
            return Array.Empty<AudioOutputDevice>();
        }

        return devices;
    }
}
