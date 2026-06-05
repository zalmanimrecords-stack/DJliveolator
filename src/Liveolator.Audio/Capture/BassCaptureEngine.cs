using ManagedBass;
using Liveolator.Core.Audio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Audio.Capture;

/// <summary>
/// Public entry point to the BASS capture backend (doc 01): enumerates capture endpoints
/// (<see cref="IAudioCaptureDeviceCatalog"/>) and hands out capture sources
/// (<see cref="IAudioCaptureSourceFactory"/>) for system-loopback / line-input. Each source owns its
/// own <see cref="BassCaptureBackend"/>, so the engine itself holds no device state and disposing a
/// source frees its record device. The App composes one engine; it is safe to keep as a singleton.
/// </summary>
public sealed class BassCaptureEngine : IAudioCaptureSourceFactory, IAudioCaptureDeviceCatalog
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<BassCaptureEngine> _logger;

    public BassCaptureEngine(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<BassCaptureEngine>();
    }

    public IReadOnlyList<AudioCaptureDevice> EnumerateCaptureDevices()
    {
        var devices = new List<AudioCaptureDevice>();
        try
        {
            for (int i = 0; Bass.RecordGetDeviceInfo(i, out DeviceInfo info); i++)
            {
                if (!info.IsEnabled)
                    continue;

                // BASS surfaces the WASAPI loopback endpoint as a record device whose name contains
                // "loopback"; everything else is treated as a hardware line-input.
                bool isLoopback = info.Name is not null &&
                                  info.Name.Contains("loopback", StringComparison.OrdinalIgnoreCase);

                devices.Add(new AudioCaptureDevice(
                    Id: i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Name: info.Name ?? $"Record device {i}",
                    Kind: isLoopback ? CaptureSourceKind.SystemLoopback : CaptureSourceKind.LineInput,
                    IsDefault: info.IsDefault));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            // Native bass absent (e.g. a dev box without the per-platform binaries): no devices.
            _logger.LogWarning(ex, "Capture device enumeration unavailable: native BASS not loaded.");
            return Array.Empty<AudioCaptureDevice>();
        }

        return devices;
    }

    public IAudioSource CreateCaptureSource(AudioCaptureDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var backend = new BassCaptureBackend(_loggerFactory.CreateLogger<BassCaptureBackend>());
        return new CaptureAudioSource(backend, device, _loggerFactory.CreateLogger<CaptureAudioSource>());
    }
}
