using System;
using System.Linq;
using Liveolator.Core.Audio;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

public class AudioLevelEnvelopeTests
{
    private static float[] Sine(int length, double amplitude, double cyclesPerWindow = 4.0)
    {
        var buffer = new float[length];
        for (int i = 0; i < length; i++)
            buffer[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * cyclesPerWindow * i / length));
        return buffer;
    }

    [Fact]
    public void Silence_ReportsZero()
    {
        var envelope = new AudioLevelEnvelope();

        VisualAudioLevel level = envelope.Process(new float[256], dtSeconds: 0.01);

        Assert.Equal(0.0, level.Rms, 6);
        Assert.Equal(0.0, level.Peak, 6);
        Assert.Equal(0.0, level.Vu, 6);
    }

    [Fact]
    public void EmptyFrame_ReportsZeroWithoutThrowing()
    {
        var envelope = new AudioLevelEnvelope();

        VisualAudioLevel level = envelope.Process(ReadOnlySpan<float>.Empty, dtSeconds: 0.01);

        Assert.Equal(0.0, level.Rms, 6);
        Assert.Equal(0.0, level.Peak, 6);
    }

    [Fact]
    public void FullScaleSine_HasExpectedRmsAndPeak()
    {
        var envelope = new AudioLevelEnvelope();

        VisualAudioLevel level = envelope.Process(Sine(2048, amplitude: 1.0), dtSeconds: 0.01);

        // RMS of a full-scale sine is 1/sqrt(2) ≈ 0.707; peak is 1.0.
        Assert.InRange(level.Rms, 0.69, 0.72);
        Assert.InRange(level.Peak, 0.99, 1.0);
    }

    [Fact]
    public void RmsAndPeak_ClampToOne_OnOverscaleInput()
    {
        var envelope = new AudioLevelEnvelope();

        VisualAudioLevel level = envelope.Process(Enumerable.Repeat(4f, 256).ToArray(), dtSeconds: 0.01);

        Assert.Equal(1.0, level.Rms, 6);
        Assert.Equal(1.0, level.Peak, 6);
    }

    [Fact]
    public void NanSamples_AreIgnored()
    {
        var envelope = new AudioLevelEnvelope();
        var buffer = new float[] { 0.5f, float.NaN, -0.5f, float.NaN };

        VisualAudioLevel level = envelope.Process(buffer, dtSeconds: 0.01);

        Assert.False(double.IsNaN(level.Rms));
        Assert.False(double.IsNaN(level.Peak));
        Assert.InRange(level.Peak, 0.49, 0.51);
    }

    [Fact]
    public void Vu_StartsAtRest_OnFirstFrame()
    {
        var envelope = new AudioLevelEnvelope();

        // First frame: dt is non-positive, so the smoothed VU stays at the floor and only RMS/peak read.
        VisualAudioLevel level = envelope.Process(Sine(2048, amplitude: 1.0), dtSeconds: 0.0);

        Assert.Equal(0.0, level.Vu, 6);
        Assert.True(level.Rms > 0.0);
    }

    [Fact]
    public void Vu_RisesFasterThanItFalls()
    {
        // Same dt for rise and fall; with fast attack / slow release the rise step must exceed the fall.
        const double dt = 0.02;
        var loud = Sine(2048, amplitude: 1.0);
        var silence = new float[2048];

        var rising = new AudioLevelEnvelope(attackSeconds: 0.05, releaseSeconds: 0.3);
        rising.Process(loud, dtSeconds: 0.0);            // seed at rest
        double afterOneLoudStep = rising.Process(loud, dtSeconds: dt).Vu;

        var falling = new AudioLevelEnvelope(attackSeconds: 0.05, releaseSeconds: 0.3);
        falling.Process(loud, dtSeconds: 0.0);
        double peakVu = falling.Process(loud, dtSeconds: 1.0).Vu;  // let it climb to ~target
        double afterOneSilentStep = falling.Process(silence, dtSeconds: dt).Vu;

        double riseDelta = afterOneLoudStep;                 // from 0 upward
        double fallDelta = peakVu - afterOneSilentStep;      // downward from the peak

        Assert.True(riseDelta > 0.0);
        Assert.True(fallDelta > 0.0);
        Assert.True(riseDelta > fallDelta,
            $"Attack step {riseDelta:F4} should exceed release step {fallDelta:F4}.");
    }

    [Fact]
    public void Vu_ConvergesTowardSustainedRms()
    {
        var envelope = new AudioLevelEnvelope(attackSeconds: 0.05, releaseSeconds: 0.3);
        var loud = Sine(2048, amplitude: 1.0);

        VisualAudioLevel level = VisualAudioLevel.Silent;
        envelope.Process(loud, dtSeconds: 0.0);
        for (int i = 0; i < 200; i++)
            level = envelope.Process(loud, dtSeconds: 0.02);

        // After ~4 s of sustained tone the smoothed VU should sit at the RMS target.
        Assert.InRange(level.Vu, level.Rms - 0.02, level.Rms + 0.02);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    public void Constructor_RejectsNonPositiveTimeConstants(double bad)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioLevelEnvelope(attackSeconds: bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioLevelEnvelope(releaseSeconds: bad));
    }
}
