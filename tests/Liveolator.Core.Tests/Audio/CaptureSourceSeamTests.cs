using System.Collections.Generic;
using Liveolator.Core.Audio;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

/// <summary>
/// Proves the capture seams (doc 01 / task 8) plug into the live pipeline: a device chosen from the
/// catalog is turned into an <see cref="IAudioSource"/> by the factory and, once set on a
/// <see cref="SwitchableAudioSource"/>, its samples reach the pipeline's subscriber — the exact path
/// the Settings/Live-tab picker will drive. Uses fakes; no native, no hardware.
/// </summary>
public class CaptureSourceSeamTests
{
    [Fact]
    public void Catalog_ExposesLoopbackAndLineInputDevices()
    {
        IAudioCaptureDeviceCatalog catalog = new FakeCaptureCatalog();

        var devices = catalog.EnumerateCaptureDevices();

        Assert.Contains(devices, d => d.Kind == CaptureSourceKind.SystemLoopback);
        Assert.Contains(devices, d => d.Kind == CaptureSourceKind.LineInput);
    }

    [Fact]
    public void SelectedDevice_ProducesSourceWhoseSamplesReachThePipeline()
    {
        IAudioCaptureDeviceCatalog catalog = new FakeCaptureCatalog();
        var factory = new FakeCaptureSourceFactory();
        var device = catalog.EnumerateCaptureDevices()[0];

        IAudioSource captureSource = factory.CreateCaptureSource(device);

        using var pipelineInput = new SwitchableAudioSource();
        var received = new List<AudioSamplesAvailable>();
        pipelineInput.SamplesAvailable += (_, e) => received.Add(e);

        pipelineInput.SetSource(captureSource); // how the picker swaps the live source
        captureSource.Start();
        ((FakeCaptureSourceFactory.FakeCaptureSource)captureSource).Emit(new float[] { 0.5f, -0.5f }, 2, 48_000);

        Assert.True(captureSource.IsRunning);
        Assert.Single(received);
        Assert.Equal(2, received[0].Channels);
    }

    private sealed class FakeCaptureCatalog : IAudioCaptureDeviceCatalog
    {
        public IReadOnlyList<AudioCaptureDevice> EnumerateCaptureDevices() => new[]
        {
            new AudioCaptureDevice("0", "Speakers (loopback)", CaptureSourceKind.SystemLoopback, IsDefault: true),
            new AudioCaptureDevice("3", "Line In", CaptureSourceKind.LineInput, IsDefault: false),
        };
    }

    private sealed class FakeCaptureSourceFactory : IAudioCaptureSourceFactory
    {
        public IAudioSource CreateCaptureSource(AudioCaptureDevice device)
        {
            ArgumentNullException.ThrowIfNull(device);
            return new FakeCaptureSource(device.Name);
        }

        internal sealed class FakeCaptureSource : IAudioSource
        {
            public FakeCaptureSource(string name) => Name = name;
            public string Name { get; }
            public bool IsRunning { get; private set; }
            public event EventHandler<AudioSamplesAvailable>? SamplesAvailable;
            public void Start() => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Emit(float[] interleaved, int channels, int sampleRate) =>
                SamplesAvailable?.Invoke(this, new AudioSamplesAvailable(interleaved, channels, sampleRate));
            public void Dispose() { }
        }
    }
}
