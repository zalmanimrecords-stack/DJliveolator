using Liveolator.Core.Audio;

namespace Liveolator.Core.Tests.Audio;

/// <summary>Test double that lets a test push raw sample batches through the pipeline.</summary>
internal sealed class FakeAudioSource : IAudioSource
{
    public string Name => "Fake";
    public bool IsRunning { get; private set; }
    public int DisposeCount { get; private set; }

    public event EventHandler<AudioSamplesAvailable>? SamplesAvailable;

    public void Start() => IsRunning = true;
    public void Stop() => IsRunning = false;

    public void Emit(float[] interleaved, int channels, int sampleRate) =>
        SamplesAvailable?.Invoke(this, new AudioSamplesAvailable(interleaved, channels, sampleRate));

    public bool HasSubscribers => SamplesAvailable is not null;

    public void Dispose() => DisposeCount++;
}
