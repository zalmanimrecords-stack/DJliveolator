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
            // Remember the device the engine is playing on so the per-device output-channel probe below
            // can restore it and never frees it (freeing the live device would kill the master output).
            int playingDevice = SafeCurrentDevice();

            // BASS device 0 is the "No sound" device — skip it; real outputs start at index 1.
            for (int i = 1; Bass.GetDeviceInfo(i, out DeviceInfo info); i++)
            {
                if (!info.IsEnabled)
                    continue;

                devices.Add(new AudioOutputDevice(
                    Id: i.ToString(CultureInfo.InvariantCulture),
                    Name: info.Name ?? $"Output device {i}",
                    IsDefault: info.IsDefault,
                    OutputChannelCount: ProbeOutputChannelCount(i, playingDevice)));
            }

            RestoreCurrentDevice(playingDevice);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            // Native bass absent: surface no devices rather than crashing (global standards #16/#26).
            _logger.LogWarning(ex, "Output device enumeration unavailable: native BASS not loaded.");
            return Array.Empty<AudioOutputDevice>();
        }

        return devices;
    }

    // Auto-detects how many output channels a card exposes (so the Settings picker can offer the real
    // 1/2 + 3/4 pairs of a CMD STUDIO 2A, not just 1/2). BASS only reports speaker count for an *open*
    // device, so a card that is not already initialised is opened briefly, probed, then freed — but the
    // device the engine is playing on (and any device we did not open) is never freed. Any hiccup folds
    // to stereo (2) rather than failing enumeration (global standards #16/#26).
    private int ProbeOutputChannelCount(int device, int playingDevice)
    {
        bool newlyOpened = false;
        try
        {
            newlyOpened = Bass.Init(device);
            // Init returns false with Already when the device is open — fine to probe; any other failure
            // means we cannot read its info, so assume stereo.
            if (!newlyOpened && Bass.LastError != Errors.Already)
                return 2;

            Bass.CurrentDevice = device;
            int speakers = Bass.GetInfo(out BassInfo info) ? info.SpeakerCount : 0;
            return speakers >= 2 ? speakers : 2;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Output-channel probe for device {Device} failed; assuming stereo.", device);
            return 2;
        }
        finally
        {
            // Only free what we opened, and never the device the engine is playing on.
            if (newlyOpened && device != playingDevice)
            {
                try
                {
                    Bass.CurrentDevice = device;
                    Bass.Free();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Freeing probe device {Device} failed.", device);
                }
            }
        }
    }

    private int SafeCurrentDevice()
    {
        try { return Bass.CurrentDevice; }
        catch { return -1; }
    }

    private void RestoreCurrentDevice(int device)
    {
        if (device < 0)
            return;
        try { Bass.CurrentDevice = device; }
        catch (Exception ex) { _logger.LogDebug(ex, "Restoring current device {Device} after probe failed.", device); }
    }
}
