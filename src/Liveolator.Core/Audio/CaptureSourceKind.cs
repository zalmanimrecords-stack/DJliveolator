namespace Liveolator.Core.Audio;

/// <summary>
/// The kind of capture an <see cref="IAudioSource"/> represents (doc 01). Both kinds produce the
/// same interleaved-float stream; the distinction is the origin, which the UI surfaces to the
/// performer and which the backend uses to open the right native device.
/// </summary>
public enum CaptureSourceKind
{
    /// <summary>The system output mix (what the speakers are playing), captured via OS loopback.</summary>
    SystemLoopback,

    /// <summary>A hardware capture input: a sound-card / audio-interface line-in or mixer feed.</summary>
    LineInput,
}
