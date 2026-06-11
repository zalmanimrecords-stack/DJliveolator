using System;
using Liveolator.Core.Waveform;
using Xunit;

namespace Liveolator.Core.Tests.Waveform;

/// <summary>
/// The biquad sections that split the waveform overview into low/mid/high bands. Each test feeds a
/// steady sine and measures the steady-state output peak (after the filter settles), so the band
/// selectivity that makes the kick layer a *kick* layer is pinned down numerically.
/// </summary>
public sealed class BiquadFilterTests
{
    private const int SampleRate = 16_000;

    [Fact]
    public void LowPass_PassesToneBelowCutoff_NearUnity()
    {
        var filter = BiquadFilter.LowPass(200, SampleRate);

        Assert.InRange(SteadyStatePeak(60, filter.Process), 0.9f, 1.1f);
    }

    [Fact]
    public void LowPass_AttenuatesToneFarAboveCutoff()
    {
        var filter = BiquadFilter.LowPass(200, SampleRate);

        // 3 kHz is ~3.9 octaves above 200 Hz; 2nd-order Butterworth ≈ -12 dB/oct → well under -40 dB.
        Assert.True(SteadyStatePeak(3_000, filter.Process) < 0.02f);
    }

    [Fact]
    public void HighPass_PassesToneAboveCutoff_NearUnity()
    {
        var filter = BiquadFilter.HighPass(2_000, SampleRate);

        Assert.InRange(SteadyStatePeak(6_000, filter.Process), 0.9f, 1.1f);
    }

    [Fact]
    public void HighPass_AttenuatesToneFarBelowCutoff()
    {
        var filter = BiquadFilter.HighPass(2_000, SampleRate);

        Assert.True(SteadyStatePeak(100, filter.Process) < 0.02f);
    }

    [Fact]
    public void MidCascade_PassesMidTone_RejectsLowAndHigh()
    {
        // The mid band as WaveformBuilder composes it: 4th-order Linkwitz-Riley edges (two identical
        // Butterworth sections per crossover), HP@200 ×2 → LP@2000 ×2. The 4th-order slope is what
        // keeps basslines out of the mid layer and kicks out of everything but the kick layer.
        float mid = CascadePeak(800);
        float low = CascadePeak(50);
        float high = CascadePeak(7_000);

        Assert.True(mid > 0.8f, $"800 Hz should pass the mid cascade, got {mid}");
        Assert.True(low < 0.02f, $"50 Hz should be rejected, got {low}");
        Assert.True(high < 0.02f, $"7 kHz should be rejected, got {high}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-200)]
    [InlineData(8_000)]  // == Nyquist at 16 kHz
    [InlineData(9_000)]  // above Nyquist
    public void Factories_RejectCutoffOutsideTheOpenNyquistInterval(double cutoffHz)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BiquadFilter.LowPass(cutoffHz, SampleRate));
        Assert.Throws<ArgumentOutOfRangeException>(() => BiquadFilter.HighPass(cutoffHz, SampleRate));
    }

    [Fact]
    public void Factories_RejectNonPositiveSampleRate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BiquadFilter.LowPass(200, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => BiquadFilter.HighPass(200, -1));
    }

    private static float CascadePeak(double frequencyHz)
    {
        var highPass1 = BiquadFilter.HighPass(200, SampleRate);
        var highPass2 = BiquadFilter.HighPass(200, SampleRate);
        var lowPass1 = BiquadFilter.LowPass(2_000, SampleRate);
        var lowPass2 = BiquadFilter.LowPass(2_000, SampleRate);
        return SteadyStatePeak(
            frequencyHz, x => lowPass2.Process(lowPass1.Process(highPass2.Process(highPass1.Process(x)))));
    }

    private static float SteadyStatePeak(double frequencyHz, Func<float, float> process)
    {
        // One settle second, then measure the peak over the next second — long enough for any
        // transient at these cutoffs to die out.
        double step = 2.0 * Math.PI * frequencyHz / SampleRate;
        float peak = 0f;
        for (int i = 0; i < SampleRate * 2; i++)
        {
            float y = process((float)Math.Sin(step * i));
            if (i >= SampleRate && Math.Abs(y) > peak)
                peak = Math.Abs(y);
        }
        return peak;
    }
}
