using System;
using Liveolator.Core.Dsp;
using Xunit;

namespace Liveolator.Core.Tests.Dsp;

/// <summary>
/// Pure-logic tests for the master brick-wall peak limiter. No BASS, no hardware: the limiter
/// processes an interleaved float span in place and is fully deterministic.
/// </summary>
public class MasterLimiterTests
{
    private const int SampleRate = 48_000;
    private const int Stereo = 2;

    private static MasterLimiter MakeStereoLimiter(double ceilingDbfs = -0.1) =>
        new(SampleRate, Stereo, ceilingDbfs);

    private static double Peak(ReadOnlySpan<float> buffer)
    {
        double peak = 0.0;
        foreach (float s in buffer)
            peak = Math.Max(peak, Math.Abs(s));
        return peak;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Ctor_RejectsNonPositiveSampleRate(int sampleRate) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new MasterLimiter(sampleRate, Stereo));

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Ctor_RejectsNonPositiveChannels(int channels) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new MasterLimiter(SampleRate, channels));

    [Fact]
    public void Ctor_RejectsCeilingAboveZeroDbfs() => // ceiling must be <= 0 dBFS
        Assert.Throws<ArgumentOutOfRangeException>(() => new MasterLimiter(SampleRate, Stereo, ceilingDbfs: 0.5));

    [Fact]
    public void SignalBelowCeiling_PassesThroughUnchanged()
    {
        var limiter = MakeStereoLimiter();
        // -6 dBFS sine, well under the -0.1 dBFS ceiling.
        const float amplitude = 0.5f;
        var buffer = new float[SampleRate * Stereo];
        for (int f = 0; f < SampleRate; f++)
        {
            float s = amplitude * (float)Math.Sin(2.0 * Math.PI * 440.0 * f / SampleRate);
            buffer[f * Stereo] = s;
            buffer[f * Stereo + 1] = s;
        }
        var expected = (float[])buffer.Clone();

        limiter.Process(buffer);

        for (int i = 0; i < buffer.Length; i++)
            Assert.Equal(expected[i], buffer[i], precision: 5);
    }

    [Fact]
    public void SignalAboveFullScale_IsLimitedToCeiling()
    {
        const double ceilingDbfs = -0.1;
        var limiter = MakeStereoLimiter(ceilingDbfs);
        double ceilingLinear = Math.Pow(10.0, ceilingDbfs / 20.0);

        // +6 dBFS worth of level: amplitude 2.0 hard-overshoots full scale.
        const float amplitude = 2.0f;
        var buffer = new float[SampleRate * Stereo];
        for (int f = 0; f < SampleRate; f++)
        {
            float s = amplitude * (float)Math.Sin(2.0 * Math.PI * 220.0 * f / SampleRate);
            buffer[f * Stereo] = s;
            buffer[f * Stereo + 1] = s;
        }

        limiter.Process(buffer);

        // Allow a tiny overshoot tolerance for the attack ramp at the very first transient;
        // measure the peak over the steady-state second half of the buffer.
        ReadOnlySpan<float> steadyState = buffer.AsSpan(buffer.Length / 2);
        double peak = Peak(steadyState);
        Assert.True(peak <= ceilingLinear + 1e-3, $"peak {peak} exceeded ceiling {ceilingLinear}");
    }

    [Fact]
    public void NeverHardClipsAboveUnity_OnExtremeInput()
    {
        var limiter = MakeStereoLimiter();
        var buffer = new float[1024 * Stereo];
        Array.Fill(buffer, 10.0f); // absurd DC-ish overload

        limiter.Process(buffer);

        // The limiter must drive everything strictly below full scale within the buffer.
        foreach (float s in buffer)
            Assert.True(Math.Abs(s) <= 1.0f, $"sample {s} hard-clipped past full scale");
    }

    [Fact]
    public void ProducesNoNaNorInfinity()
    {
        var limiter = MakeStereoLimiter();
        var buffer = new float[2048 * Stereo];
        var rng = new Random(42);
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = (float)(rng.NextDouble() * 8.0 - 4.0); // wildly hot, both signs

        limiter.Process(buffer);

        foreach (float s in buffer)
        {
            Assert.False(float.IsNaN(s), "limiter produced NaN");
            Assert.False(float.IsInfinity(s), "limiter produced Infinity");
        }
    }

    [Fact]
    public void StereoChannelsShareOneGain_ImageIsPreserved()
    {
        var limiter = MakeStereoLimiter();
        // Identical L and R: a stereo-linked limiter must keep them identical after limiting,
        // otherwise the stereo image (here mono-centre) would shift.
        var buffer = new float[512 * Stereo];
        for (int f = 0; f < 512; f++)
        {
            float s = 1.8f * (float)Math.Sin(2.0 * Math.PI * 110.0 * f / SampleRate);
            buffer[f * Stereo] = s;
            buffer[f * Stereo + 1] = s;
        }

        limiter.Process(buffer);

        for (int f = 0; f < 512; f++)
            Assert.Equal(buffer[f * Stereo], buffer[f * Stereo + 1], precision: 6);
    }

    [Fact]
    public void Release_RecoversGainTowardUnityAfterLoudPassage()
    {
        var limiter = MakeStereoLimiter();

        // Drive it hard so gain reduction kicks in.
        var loud = new float[SampleRate * Stereo];
        Array.Fill(loud, 2.0f);
        limiter.Process(loud);
        double reducedGain = limiter.CurrentGain;
        Assert.True(reducedGain < 1.0, "expected gain reduction during loud passage");

        // Then feed a long quiet passage; release should let gain climb back toward unity.
        var quiet = new float[SampleRate * Stereo];
        Array.Fill(quiet, 0.01f);
        limiter.Process(quiet);

        Assert.True(limiter.CurrentGain > reducedGain, "gain did not recover during quiet passage");
        Assert.True(limiter.CurrentGain <= 1.0 + 1e-6, "gain overshot unity");
    }

    [Fact]
    public void Process_RejectsBufferNotAMultipleOfChannelCount()
    {
        var limiter = MakeStereoLimiter();
        var odd = new float[Stereo * 3 + 1];
        Assert.Throws<ArgumentException>(() => limiter.Process(odd));
    }

    [Fact]
    public void Process_EmptyBuffer_IsNoOp()
    {
        var limiter = MakeStereoLimiter();
        limiter.Process(Array.Empty<float>()); // must not throw
        Assert.Equal(1.0, limiter.CurrentGain, precision: 6);
    }
}
