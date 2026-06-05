namespace Liveolator.Core.Audio;

/// <summary>
/// Creates an <see cref="IAudioSource"/> that captures a selected endpoint (doc 01) — system
/// loopback or a hardware line-input — so the same frame pipeline + beat clock can be fed from
/// outside-the-app audio. The BASS-backed factory lives in Liveolator.Audio; a fake supplies test
/// sources. Mirrors <see cref="IDeckSourceFactory"/> so source selection stays platform-independent.
/// </summary>
public interface IAudioCaptureSourceFactory
{
    /// <summary>
    /// Create (but do not start) a capture source for the given device. The returned source emits
    /// <see cref="AudioSamplesAvailable"/> exactly like a deck, so it plugs straight into the
    /// existing pipeline. Throws <see cref="ArgumentNullException"/> if <paramref name="device"/> is null.
    /// </summary>
    IAudioSource CreateCaptureSource(AudioCaptureDevice device);
}
