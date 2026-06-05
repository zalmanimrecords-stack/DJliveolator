namespace Liveolator.Core.Audio;

/// <summary>
/// Realtime audio source seam (doc 01): produces a normalized stream of raw samples regardless
/// of origin (deck playback, system loopback, sound-card input). Core depends only on this
/// interface; the concrete native backend (BASS on Win/macOS) lives in Liveolator.Audio, so this
/// layer stays platform-independent and unit-tests with a fake source.
/// </summary>
/// <remarks>
/// A source only <em>produces</em> raw samples. Turning them into analysis-ready frames (FFT,
/// waveform) is the frame pipeline's job (<see cref="IAudioFrameProvider"/>, doc 02) — strict
/// layer separation.
/// </remarks>
public interface IAudioSource : IDisposable
{
    /// <summary>Human-readable name of the source (device or deck label).</summary>
    string Name { get; }

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    bool IsRunning { get; }

    /// <summary>Begin producing samples. Idempotent: starting a running source is a no-op.</summary>
    void Start();

    /// <summary>Stop producing samples. Idempotent: stopping a stopped source is a no-op.</summary>
    void Stop();

    /// <summary>
    /// Raised on the capture/playback thread when new samples are available. Handlers must be
    /// fast and non-blocking; heavy work belongs on the consumer's own loop.
    /// </summary>
    event EventHandler<AudioSamplesAvailable>? SamplesAvailable;
}
