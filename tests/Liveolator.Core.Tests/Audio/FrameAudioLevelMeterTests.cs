using System;
using Liveolator.Core.Audio;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

public class FrameAudioLevelMeterTests
{
    /// <summary>A frame provider a test pumps synthetic frames through.</summary>
    private sealed class FakeFrameProvider : IAudioFrameProvider
    {
        private AudioFrameData _latest = AudioFrameData.Empty;
        public event EventHandler<AudioFrameData>? FrameAvailable;
        public AudioFrameData GetLatestFrame() => _latest;
        public int SubscriberCount => FrameAvailable?.GetInvocationList().Length ?? 0;

        public void Emit(AudioFrameData frame)
        {
            _latest = frame;
            FrameAvailable?.Invoke(this, frame);
        }
    }

    private static AudioFrameData Frame(long index, double t, float[] mono) => new(
        MonoPcm: mono,
        Spectrum: Array.Empty<float>(),
        Waveform: Array.Empty<float>(),
        SampleRate: 48_000,
        FrameIndex: index,
        TimestampSeconds: t);

    private static float[] Constant(int length, float value)
    {
        var buffer = new float[length];
        Array.Fill(buffer, value);
        return buffer;
    }

    [Fact]
    public void Current_IsSilent_BeforeAnyFrame()
    {
        using var meter = new FrameAudioLevelMeter(new FakeFrameProvider());

        Assert.Equal(VisualAudioLevel.Silent, meter.Current);
    }

    [Fact]
    public void Current_TracksEmittedFrames()
    {
        var provider = new FakeFrameProvider();
        using var meter = new FrameAudioLevelMeter(provider);

        provider.Emit(Frame(0, 0.00, Constant(256, 0.5f)));
        provider.Emit(Frame(1, 0.01, Constant(256, 0.5f)));

        Assert.InRange(meter.Current.Rms, 0.49, 0.51);
        Assert.InRange(meter.Current.Peak, 0.49, 0.51);
        Assert.True(meter.Current.Vu > 0.0);
    }

    [Fact]
    public void EmptyAndPrimingFrames_AreIgnored()
    {
        var provider = new FakeFrameProvider();
        using var meter = new FrameAudioLevelMeter(provider);

        provider.Emit(AudioFrameData.Empty);                       // FrameIndex -1
        provider.Emit(Frame(0, 0.0, Array.Empty<float>()));        // no mono feed

        Assert.Equal(VisualAudioLevel.Silent, meter.Current);
    }

    [Fact]
    public void Dispose_Unsubscribes()
    {
        var provider = new FakeFrameProvider();
        var meter = new FrameAudioLevelMeter(provider);
        Assert.Equal(1, provider.SubscriberCount);

        meter.Dispose();

        Assert.Equal(0, provider.SubscriberCount);
    }

    [Fact]
    public void Constructor_RejectsNullProvider()
    {
        Assert.Throws<ArgumentNullException>(() => new FrameAudioLevelMeter(null!));
    }
}
