using Liveolator.Core.Audio;

namespace Liveolator.Core.Settings;

/// <summary>
/// User-chosen realtime audio settings (doc 01): which sound-card output device drives the master mix,
/// the output buffer length that trades latency against glitch-resistance, and the optional capture
/// source (system-loopback / line-in) fed into the live pipeline. Pure data the App persists and the
/// audio binding applies when it initialises BASS — Core defines the model so the Settings UI and the
/// binding agree on one contract and it unit-tests with no native code.
/// </summary>
/// <remarks>
/// <see cref="OutputDeviceId"/> is the backend-opaque id of an <see cref="AudioOutputDevice"/>
/// (null = the platform default device). <see cref="BufferMilliseconds"/> is clamped by
/// <see cref="Normalized"/> into <see cref="MinBufferMs"/>..<see cref="MaxBufferMs"/> so a stale or
/// hand-edited config can never push an out-of-range value into the audio device.
/// <see cref="CaptureDeviceId"/> + <see cref="CaptureSource"/> are the persisted capture-source choice
/// (null id = no capture source selected); both are folded together so a half-written config (an id with
/// no kind, or a kind with no id) normalizes to "no capture" rather than an inconsistent selection.
/// </remarks>
public sealed record AudioSettings
{
    /// <summary>Smallest output buffer offered (ms) — the low-latency end, more glitch-prone.</summary>
    public const int MinBufferMs = 10;

    /// <summary>Largest output buffer offered (ms) — the safe end, more latency.</summary>
    public const int MaxBufferMs = 200;

    /// <summary>Default output buffer (ms): a balance suitable for DJ use without obvious lag.</summary>
    public const int DefaultBufferMs = 40;

    /// <summary>Backend-opaque output device id, or null for the platform default device.</summary>
    public string? OutputDeviceId { get; init; }

    /// <summary>
    /// Backend-opaque output device id for the headphone-cue (PFL) output, or null when no separate
    /// cue output is configured (cue then has nowhere to play). On the CMD STUDIO 2A's built-in 4-ch
    /// interface this is the same device as <see cref="OutputDeviceId"/> with the cue on channels 3/4;
    /// on a separate headphone interface it is a distinct device. Never hardcoded — the user picks it.
    /// </summary>
    public string? CueOutputDeviceId { get; init; }

    /// <summary>Requested output buffer length in milliseconds (see <see cref="Normalized"/>).</summary>
    public int BufferMilliseconds { get; init; } = DefaultBufferMs;

    /// <summary>
    /// Which stereo output pair on the master device drives the main mix: 0 = the card's outputs 1/2,
    /// 1 = 3/4, etc. (see <see cref="OutputChannelPair"/>). Lets a multi-output card send the master to
    /// one pair and the headphone-cue to another. <see cref="Normalized"/> clamps it to the addressable
    /// range so a stale config can never request a non-existent pair.
    /// </summary>
    public int MasterOutputPair { get; init; }

    /// <summary>
    /// Which stereo output pair on the cue device carries the headphones (PFL). On the CMD STUDIO 2A
    /// this is pair 1 (outputs 3/4) of the same card as the master; on a separate headphone interface
    /// it is that device's pair 0. Clamped by <see cref="Normalized"/> like <see cref="MasterOutputPair"/>.
    /// </summary>
    public int CueOutputPair { get; init; }

    /// <summary>
    /// Backend-opaque capture device id (an <see cref="AudioCaptureDevice.Id"/>), or null when no
    /// capture source is selected. Blank ids fold to null via <see cref="Normalized"/>.
    /// </summary>
    public string? CaptureDeviceId { get; init; }

    /// <summary>
    /// The kind of the selected capture source (loopback / line-in), or null when none is selected.
    /// Folded together with <see cref="CaptureDeviceId"/> so the pair is always internally consistent.
    /// </summary>
    public CaptureSourceKind? CaptureSource { get; init; }

    /// <summary>
    /// Experimental stem decks (doc 32 §Phase 2b): when true, loading a track that has a complete locally-
    /// cached stem set opens it as a 4-stem submix so each stem can be muted, instead of the single file.
    /// Default off. Read once at engine construction, so a change takes effect on the next launch (like the
    /// output device). Off ⇒ every deck load is the normal single file — the stem UI stays disabled.
    /// </summary>
    public bool StemsEnabled { get; init; }

    /// <summary>The defaults: platform-default device, <see cref="DefaultBufferMs"/> buffer, no capture.</summary>
    public static AudioSettings Default { get; } = new();

    /// <summary>
    /// Returns a copy with <see cref="BufferMilliseconds"/> clamped to the supported range, the master
    /// and cue output pairs clamped to the addressable range, a blank cue device id folded to null
    /// (else trimmed), and the capture-source choice folded to a consistent state: a blank id, or an id
    /// without a kind, or a kind without an id, all normalize to "no capture source".
    /// </summary>
    public AudioSettings Normalized()
    {
        bool hasCapture = !string.IsNullOrWhiteSpace(CaptureDeviceId) && CaptureSource is not null;
        return this with
        {
            BufferMilliseconds = Math.Clamp(BufferMilliseconds, MinBufferMs, MaxBufferMs),
            CueOutputDeviceId = string.IsNullOrWhiteSpace(CueOutputDeviceId) ? null : CueOutputDeviceId.Trim(),
            MasterOutputPair = Math.Clamp(MasterOutputPair, 0, OutputChannelPair.MaxPairIndex),
            CueOutputPair = Math.Clamp(CueOutputPair, 0, OutputChannelPair.MaxPairIndex),
            CaptureDeviceId = hasCapture ? CaptureDeviceId!.Trim() : null,
            CaptureSource = hasCapture ? CaptureSource : null,
        };
    }
}
