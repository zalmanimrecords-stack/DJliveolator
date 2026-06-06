namespace Liveolator.Core.Settings;

/// <summary>
/// User-chosen realtime audio output settings (doc 01): which sound-card output device drives the
/// master mix, and the output buffer length that trades latency against glitch-resistance. Pure data
/// the App persists and the audio binding applies when it initialises BASS — Core defines the model so
/// the Settings UI and the binding agree on one contract and it unit-tests with no native code.
/// </summary>
/// <remarks>
/// <see cref="OutputDeviceId"/> is the backend-opaque id of an <see cref="Audio.AudioOutputDevice"/>
/// (null = the platform default device). <see cref="BufferMilliseconds"/> is clamped by
/// <see cref="Normalized"/> into <see cref="MinBufferMs"/>..<see cref="MaxBufferMs"/> so a stale or
/// hand-edited config can never push an out-of-range value into the audio device.
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

    /// <summary>The defaults: platform-default device, <see cref="DefaultBufferMs"/> buffer.</summary>
    public static AudioSettings Default { get; } = new();

    /// <summary>Returns a copy with <see cref="BufferMilliseconds"/> clamped to the supported range.</summary>
    public AudioSettings Normalized()
        => this with { BufferMilliseconds = Math.Clamp(BufferMilliseconds, MinBufferMs, MaxBufferMs) };
}
