using Liveolator.Core.Audio;

namespace Liveolator.Audio.Playback;

/// <summary>
/// The post-crossfader master mix exposed as an <see cref="IAudioSource"/> (doc 11): the two-deck
/// engine's BASSmix tap feeds <see cref="Emit"/>, and the frame pipeline (doc 02) subscribes to
/// <see cref="SamplesAvailable"/> so the beat clock sees exactly the audible mix. Created and driven by
/// <see cref="TwoDeckBassEngine"/>; the stamped format is the master channel's format, fixed for its
/// lifetime.
/// </summary>
internal sealed class MasterAudioSource : IAudioSource
{
    private readonly int _channels;
    private readonly int _sampleRate;
    private bool _running;

    public MasterAudioSource(int channels, int sampleRate)
    {
        if (channels < 1)
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Master must have at least one channel.");
        if (sampleRate < 1)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Master sample rate must be positive.");
        _channels = channels;
        _sampleRate = sampleRate;
    }

    public string Name => "Master Mix";

    public bool IsRunning => _running;

    public event EventHandler<AudioSamplesAvailable>? SamplesAvailable;

    public void Start() => _running = true;

    public void Stop() => _running = false;

    /// <summary>Deliver a batch of mixed samples from the master tap to the frame pipeline.</summary>
    public void Emit(float[] interleaved)
    {
        if (interleaved is null || interleaved.Length == 0)
            return;
        SamplesAvailable?.Invoke(this, new AudioSamplesAvailable(interleaved, _channels, _sampleRate));
    }

    public void Dispose() => _running = false;
}
