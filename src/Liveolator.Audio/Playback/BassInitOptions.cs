using System;
using System.Globalization;
using Liveolator.Core.Audio;
using Liveolator.Core.Settings;
using ManagedBass;

namespace Liveolator.Audio.Playback;

/// <summary>
/// The native BASS initialisation parameters resolved from the user's persisted
/// <see cref="AudioSettings"/> (doc 01/12): which output device index to open, the playback buffer
/// length, and which output channel-pair (speaker assignment) the master and the headphone-cue use.
/// Kept as a pure value so the mapping from the backend-opaque device id (a BASS device-index string —
/// see <see cref="BassOutputDeviceCatalog"/>) back to a BASS device number + speaker flag is unit-tested
/// with no native BASS, mirroring how the rest of the audio binding isolates interop.
/// </summary>
internal readonly record struct BassInitOptions(
    int DeviceIndex, int CueDeviceIndex, int BufferMilliseconds, int MasterOutputPair, int CueOutputPair)
{
    // BASS speaker assignment encodes the (1-based) pair number in bits 24+: SpeakerPairN == N << 24.
    private const int SpeakerPairShift = 24;
    /// <summary>The <c>Bass.Init</c> sentinel for "use the system default output device".</summary>
    public const int DefaultDevice = -1;

    /// <summary>Sentinel for "no separate headphone-cue output configured".</summary>
    public const int NoCueDevice = 0;

    /// <summary>
    /// The BASS automatic-update period (ms) to apply alongside the playback buffer. BASS refills the
    /// device playback buffer on this period; it MUST stay comfortably below <see cref="BufferMilliseconds"/>
    /// or the buffer starves between refills and playback runs slow — a low buffer left with BASS's 100 ms
    /// default period plays at roughly buffer/period speed (e.g. a 40 ms buffer ran at ~0.4×). A quarter of
    /// the buffer, clamped to 5..20 ms, keeps a safe refill margin while preserving low DJ latency.
    /// </summary>
    public int UpdatePeriodMilliseconds => Math.Clamp(BufferMilliseconds / 4, 5, 20);

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
            normalized.BufferMilliseconds,
            normalized.MasterOutputPair,
            normalized.CueOutputPair);
    }

    /// <summary>True when a separate headphone-cue output device has been configured.</summary>
    public bool HasCueDevice => CueDeviceIndex >= 1;

    /// <summary>The BASS speaker-assignment flag the master mixer is created with.</summary>
    public BassFlags MasterSpeakerFlag => SpeakerFlag(MasterOutputPair);

    /// <summary>The BASS speaker-assignment flag the headphone-cue mixer + per-deck cue pushes use.</summary>
    public BassFlags CueSpeakerFlag => SpeakerFlag(CueOutputPair);

    /// <summary>
    /// Resolves a 0-based output channel-pair index to its BASS speaker-assignment flag. Pair 0
    /// (outputs 1/2) keeps BASS's default stereo routing (no flag), so the common single-output path is
    /// byte-for-byte the prior behaviour; pair 1+ sets <c>SpeakerPairN</c> (N = pair + 1) to route the
    /// stream to outputs 3/4, 5/6 or 7/8. The index is clamped so a bad value can never form a bogus flag.
    /// </summary>
    internal static BassFlags SpeakerFlag(int pairIndex)
    {
        int clamped = Math.Clamp(pairIndex, 0, OutputChannelPair.MaxPairIndex);
        return clamped == 0 ? BassFlags.Default : (BassFlags)((clamped + 1) << SpeakerPairShift);
    }

    private static int ParseDeviceIndex(string? deviceId)
        => int.TryParse(deviceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && index >= 1
            ? index
            : DefaultDevice;

    private static int ParseCueDeviceIndex(string? deviceId)
        => int.TryParse(deviceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && index >= 1
            ? index
            : NoCueDevice;
}
