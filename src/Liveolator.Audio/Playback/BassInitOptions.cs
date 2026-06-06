using System;
using System.Globalization;
using Liveolator.Core.Settings;

namespace Liveolator.Audio.Playback;

/// <summary>
/// The native BASS initialisation parameters resolved from the user's persisted
/// <see cref="AudioSettings"/> (doc 01/12): which output device index to open and the playback buffer
/// length to apply. Kept as a pure value so the mapping from the backend-opaque device id (a BASS
/// device-index string — see <see cref="BassOutputDeviceCatalog"/>) back to a BASS device number is
/// unit-tested with no native BASS, mirroring how the rest of the audio binding isolates interop.
/// </summary>
internal readonly record struct BassInitOptions(int DeviceIndex, int CueDeviceIndex, int BufferMilliseconds)
{
    /// <summary>The <c>Bass.Init</c> sentinel for "use the system default output device".</summary>
    public const int DefaultDevice = -1;

    /// <summary>Sentinel for "no separate headphone-cue output configured".</summary>
    public const int NoCueDevice = 0;

    /// <summary>
    /// Resolves the init parameters from settings (null = <see cref="AudioSettings.Default"/>). The
    /// buffer is clamped to the supported range and the device id is parsed back to its BASS index; a
    /// null / blank / non-numeric id — or the reserved "No sound" device 0 — folds to
    /// <see cref="DefaultDevice"/> so a stale saved choice never opens a bogus device. The cue device
    /// id resolves the same way but falls to <see cref="NoCueDevice"/> when unset (cue stays silent
    /// until the user picks an output).
    /// </summary>
    public static BassInitOptions From(AudioSettings? settings)
    {
        AudioSettings normalized = (settings ?? AudioSettings.Default).Normalized();
        return new BassInitOptions(
            ParseDeviceIndex(normalized.OutputDeviceId),
            ParseCueDeviceIndex(normalized.CueOutputDeviceId),
            normalized.BufferMilliseconds);
    }

    /// <summary>True when a separate headphone-cue output device has been configured.</summary>
    public bool HasCueDevice => CueDeviceIndex >= 1;

    /// <summary>
    /// The BASS automatic-update period (ms) to apply alongside the playback buffer. BASS refills the
    /// device playback buffer on this period; it MUST stay comfortably below <see cref="BufferMilliseconds"/>
    /// or the buffer starves between refills and playback runs slow — a low buffer left with BASS's 100 ms
    /// default period plays at roughly buffer/period speed (e.g. a 40 ms buffer ran at ~0.4×). A quarter of
    /// the buffer, clamped to 5..20 ms, keeps a safe refill margin while preserving low DJ latency.
    /// </summary>
    public int UpdatePeriodMilliseconds => Math.Clamp(BufferMilliseconds / 4, 5, 20);

    private static int ParseDeviceIndex(string? deviceId)
        => int.TryParse(deviceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && index >= 1
            ? index
            : DefaultDevice;

    private static int ParseCueDeviceIndex(string? deviceId)
        => int.TryParse(deviceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && index >= 1
            ? index
            : NoCueDevice;
}
