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
internal readonly record struct BassInitOptions(int DeviceIndex, int BufferMilliseconds)
{
    /// <summary>The <c>Bass.Init</c> sentinel for "use the system default output device".</summary>
    public const int DefaultDevice = -1;

    /// <summary>
    /// Resolves the init parameters from settings (null = <see cref="AudioSettings.Default"/>). The
    /// buffer is clamped to the supported range and the device id is parsed back to its BASS index; a
    /// null / blank / non-numeric id — or the reserved "No sound" device 0 — folds to
    /// <see cref="DefaultDevice"/> so a stale saved choice never opens a bogus device.
    /// </summary>
    public static BassInitOptions From(AudioSettings? settings)
    {
        AudioSettings normalized = (settings ?? AudioSettings.Default).Normalized();
        return new BassInitOptions(ParseDeviceIndex(normalized.OutputDeviceId), normalized.BufferMilliseconds);
    }

    private static int ParseDeviceIndex(string? deviceId)
        => int.TryParse(deviceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && index >= 1
            ? index
            : DefaultDevice;
}
