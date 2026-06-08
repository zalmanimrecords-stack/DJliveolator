using System;
using Liveolator.Core.Dsp;
using Xunit;

namespace Liveolator.Core.Tests.Dsp;

/// <summary>
/// Pure-logic tests for the true-peak look-ahead master brick-wall limiter. No BASS, no hardware: the
/// limiter processes an interleaved float span in place and is fully deterministic. The output is the
/// input delayed by <see cref="MasterLimiter.LatencySamples"/>, so tests that compare to the input align
/// by that latency.
/// </summary>
public class MasterLimiterTests
{
    private const int SampleRate = 48_000;
    private const int Stereo = 2;

    private static MasterLimiter MakeStereoLimiter(double ceilingDbTp = -1.0) =>
        new(SampleRate, Stereo, ceilingDbTp);

    private static double CeilingLinear(double dbtp) => Math.Pow(10.0, dbtp / 20.0);

    private static double Peak(ReadOnlySpan<float> buffer)
    {
        double peak = 0.0;
        foreach (float s in buffer)
            peak = Math.Max(peak, Math.Abs(s));
        return peak;
    }

    // Reference inter-sample (true) peak of a mono signal via high-quality windowed-sinc oversampling —
    // independent of the limiter's own detector, so it fairly judges the limiter's output.
    private static double ReferenceTruePeak(ReadOnlySpan<float> mono, int oversample = 8, int taps = 16)
    {
        double peak = 0.0;
        int center = taps / 2;
        for (int n = 0; n < mono.Length; n++)
        {
            for (int p = 0; p < oversample; p++)
            {
                double frac = (double)p / oversample;
                double acc = 0.0;
                for (int t = 0; t < taps; t++)
                {
                    int idx = n - center + 1 + t;
                    if (idx < 0 || idx >= mono.Length)
                        continue;
                    double x = (center - 1 - t) + frac;
                    acc += mono[idx] * Sinc(x) * Hann(t, taps);
                }
                peak = Math.Max(peak, Math.Abs(acc));
            }
        }
        return peak;
    }

    private static double Sinc(double x)
    {
        if (Math.Abs(x) < 1e-9) return 1.0;
        double px = Math.PI * x;
        return Math.Sin(px) / px;
    }

    private static double Hann(int t, int length) => 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * t / (length - 1));

    // --- Constructor validation ---------------------------------------------------------------------

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
    public void Ctor_RejectsCeilingAboveZeroDb() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new MasterLimiter(SampleRate, Stereo, ceilingDbTp: 0.5));

    // --- Latency ------------------------------------------------------------------------------------

    [Fact]
    public void LatencySamples_EqualsLookaheadWindow()
    {
        var limiter = MakeStereoLimiter();
        int expected = (int)Math.Round(5.0 * 0.001 * SampleRate); // default 5 ms look-ahead
        Assert.Equal(expected, limiter.LatencySamples);
    }

    [Fact]
    public void Impulse_BelowCeiling_AppearsDelayedByLatency_AndUnscaled()
    {
        var limiter = MakeStereoLimiter();
        int L = limiter.LatencySamples;
        const int frames = 2048;
        const float amp = 0.5f; // below the ceiling → no gain reduction, exact passthrough (delayed)
        var buffer = new float[frames * Stereo];
        buffer[10 * Stereo] = amp;
        buffer[10 * Stereo + 1] = amp;

        limiter.Process(buffer);

        Assert.Equal(amp, buffer[(10 + L) * Stereo], precision: 5);
        Assert.Equal(amp, buffer[(10 + L) * Stereo + 1], precision: 5);
        // Everything before the delayed impulse is silent (the primed look-ahead tail).
        for (int i = 0; i < (10 + L) * Stereo; i++)
            Assert.Equal(0f, buffer[i], precision: 6);
    }

    // --- Passthrough & limiting ---------------------------------------------------------------------

    [Fact]
    public void SignalBelowCeiling_PassesThroughUnchanged_DelayAligned()
    {
        var limiter = MakeStereoLimiter();
        int L = limiter.LatencySamples;
        const float amplitude = 0.5f; // −6 dBFS, well under the ceiling
        var input = new float[SampleRate * Stereo];
        for (int f = 0; f < SampleRate; f++)
        {
            float s = amplitude * (float)Math.Sin(2.0 * Math.PI * 440.0 * f / SampleRate);
            input[f * Stereo] = s;
            input[f * Stereo + 1] = s;
        }
        var buffer = (float[])input.Clone();

        limiter.Process(buffer);

        // Output frame i (i >= L) equals input frame i-L; gain stayed at unity.
        for (int f = L; f < SampleRate; f++)
        {
            Assert.Equal(input[(f - L) * Stereo], buffer[f * Stereo], precision: 5);
            Assert.Equal(input[(f - L) * Stereo + 1], buffer[f * Stereo + 1], precision: 5);
        }
        Assert.Equal(1.0, limiter.CurrentGain, precision: 6);
    }

    [Fact]
    public void SignalAboveFullScale_IsLimitedToCeiling()
    {
        const double ceilingDbTp = -1.0;
        var limiter = MakeStereoLimiter(ceilingDbTp);
        double ceiling = CeilingLinear(ceilingDbTp);

        const float amplitude = 2.0f; // +6 dB over full scale
        var buffer = new float[SampleRate * Stereo];
        for (int f = 0; f < SampleRate; f++)
        {
            float s = amplitude * (float)Math.Sin(2.0 * Math.PI * 220.0 * f / SampleRate);
            buffer[f * Stereo] = s;
            buffer[f * Stereo + 1] = s;
        }

        limiter.Process(buffer);

        // Steady-state peak (second half, past the priming + attack ramp) is at/under the ceiling.
        double peak = Peak(buffer.AsSpan(buffer.Length / 2));
        Assert.True(peak <= ceiling + 1e-3, $"peak {peak} exceeded ceiling {ceiling}");
    }

    [Fact]
    public void InterSamplePeak_IsCaught_WhereASamplePeakLimiterWouldClip()
    {
        const double ceilingDbTp = -1.0;
        var limiter = MakeStereoLimiter(ceilingDbTp);
        double ceiling = CeilingLinear(ceilingDbTp);

        // fs/4 tone at 45°: samples land at ±0.707 (below the ceiling) but the reconstructed waveform
        // peaks at 1.0 (above it). A sample-peak limiter sees 0.707 < ceiling and does nothing → ISP clip.
        const int frames = 8192;
        var buffer = new float[frames * Stereo];
        for (int f = 0; f < frames; f++)
        {
            float s = (float)Math.Cos(Math.PI * f / 2.0 + Math.PI / 4.0); // amplitude 1.0
            buffer[f * Stereo] = s;
            buffer[f * Stereo + 1] = s;
        }
        double inputSamplePeak = Peak(buffer);
        Assert.True(inputSamplePeak < ceiling, "test setup: input SAMPLE peak must be below the ceiling");

        limiter.Process(buffer);

        // The true-peak detector must have engaged (a sample-peak limiter would leave gain at unity).
        Assert.True(limiter.CurrentGain < 0.95, $"true-peak detector did not engage (gain {limiter.CurrentGain})");

        // Reconstruct the limited output's true peak independently; it must sit at/near the ceiling, far
        // below the 1.0 it would have clipped to. Steady-state region (past priming).
        var monoOut = new float[frames - 512];
        for (int f = 0; f < monoOut.Length; f++)
            monoOut[f] = buffer[(f + 512) * Stereo];
        double outTruePeak = ReferenceTruePeak(monoOut);
        Assert.True(outTruePeak <= ceiling * 1.07, $"output true peak {outTruePeak} exceeded ceiling {ceiling}");
    }

    [Fact]
    public void LookAhead_PreventsTransientClipping()
    {
        const double ceilingDbTp = -1.0;
        var limiter = MakeStereoLimiter(ceilingDbTp);
        double ceiling = CeilingLinear(ceilingDbTp);
        int L = limiter.LatencySamples;

        // Silence, a hot transient burst, then silence. With look-ahead the gain is already reduced when
        // the burst reaches the output, so no output sample exceeds the ceiling (no hard-clamp distortion).
        const int frames = 4096;
        int burstStart = 1000;
        var buffer = new float[frames * Stereo];
        for (int f = burstStart; f < burstStart + 64; f++)
        {
            buffer[f * Stereo] = 4.0f;       // +12 dB
            buffer[f * Stereo + 1] = 4.0f;
        }

        limiter.Process(buffer);

        foreach (float s in buffer)
            Assert.True(Math.Abs(s) <= ceiling + 1e-3, $"sample {s} exceeded ceiling {ceiling}");
        // The burst emerges at output index burstStart + L; gain must already be well under unity there.
        // (We can't read historical gain, but the output peak proves the pre-emption held.)
        double burstOut = Peak(buffer.AsSpan((burstStart + L) * Stereo, 64 * Stereo));
        Assert.True(burstOut <= ceiling + 1e-3 && burstOut > 0.0, $"burst output {burstOut} mis-limited");
    }

    [Fact]
    public void NeverExceedsCeiling_OnExtremeInput()
    {
        var limiter = MakeStereoLimiter();
        double ceiling = CeilingLinear(-1.0);
        var buffer = new float[4096 * Stereo];
        Array.Fill(buffer, 10.0f); // absurd DC-ish overload

        limiter.Process(buffer);

        foreach (float s in buffer)
            Assert.True(Math.Abs(s) <= ceiling + 1e-3, $"sample {s} exceeded ceiling {ceiling}");
    }

    [Fact]
    public void NonFiniteInput_ProducesFiniteOutput()
    {
        var limiter = MakeStereoLimiter();
        var buffer = new float[1024 * Stereo];
        var rng = new Random(42);
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = (float)(rng.NextDouble() * 8.0 - 4.0);
        buffer[100] = float.NaN;
        buffer[201] = float.PositiveInfinity;
        buffer[303] = float.NegativeInfinity;

        limiter.Process(buffer);

        foreach (float s in buffer)
        {
            Assert.False(float.IsNaN(s), "limiter produced NaN");
            Assert.False(float.IsInfinity(s), "limiter produced Infinity");
        }
    }

    // --- Stereo linking & N channels ----------------------------------------------------------------

    [Fact]
    public void StereoChannelsShareOneGain_ImageIsPreserved()
    {
        var limiter = MakeStereoLimiter();
        var buffer = new float[2048 * Stereo];
        for (int f = 0; f < 2048; f++)
        {
            float s = 1.8f * (float)Math.Sin(2.0 * Math.PI * 110.0 * f / SampleRate);
            buffer[f * Stereo] = s;
            buffer[f * Stereo + 1] = s;
        }

        limiter.Process(buffer);

        for (int f = 0; f < 2048; f++)
            Assert.Equal(buffer[f * Stereo], buffer[f * Stereo + 1], precision: 6);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void WorksForMonoAndMultiChannel(int channels)
    {
        var limiter = new MasterLimiter(SampleRate, channels);
        double ceiling = CeilingLinear(-1.0);
        var buffer = new float[1024 * channels];
        Array.Fill(buffer, 3.0f);

        limiter.Process(buffer);

        foreach (float s in buffer)
            Assert.True(Math.Abs(s) <= ceiling + 1e-3);
    }

    // --- Release & continuity -----------------------------------------------------------------------

    [Fact]
    public void Release_RecoversGainTowardUnityAfterLoudPassage()
    {
        var limiter = MakeStereoLimiter();

        var loud = new float[SampleRate * Stereo];
        Array.Fill(loud, 2.0f);
        limiter.Process(loud);
        double reducedGain = limiter.CurrentGain;
        Assert.True(reducedGain < 1.0, "expected gain reduction during loud passage");

        var quiet = new float[SampleRate * Stereo];
        Array.Fill(quiet, 0.01f);
        limiter.Process(quiet);

        Assert.True(limiter.CurrentGain > reducedGain, "gain did not recover during quiet passage");
        Assert.True(limiter.CurrentGain <= 1.0 + 1e-6, "gain overshot unity");
    }

    [Fact]
    public void GainAndDelayAreContinuousAcrossArbitraryBufferSplits()
    {
        // The same signal processed whole vs. in odd-sized chunks must produce identical output — proving
        // all state (gain, delay ring, detector history, gain-window deque) carries across Process calls.
        const int frames = 6000;
        var signal = new float[frames * Stereo];
        var rng = new Random(7);
        for (int f = 0; f < frames; f++)
        {
            float s = (float)(Math.Sin(2.0 * Math.PI * 130.0 * f / SampleRate) * 1.6 + (rng.NextDouble() - 0.5) * 0.4);
            signal[f * Stereo] = s;
            signal[f * Stereo + 1] = s * 0.8f;
        }

        var whole = (float[])signal.Clone();
        new MasterLimiter(SampleRate, Stereo).Process(whole);

        var split = (float[])signal.Clone();
        var chunked = new MasterLimiter(SampleRate, Stereo);
        int[] chunkFrames = { 1, 3, 17, 256, 999, 1, 4096 };
        int offset = 0, ci = 0;
        while (offset < split.Length)
        {
            int framesThis = Math.Min(chunkFrames[ci % chunkFrames.Length], (split.Length - offset) / Stereo);
            ci++;
            if (framesThis <= 0) break;
            chunked.Process(split.AsSpan(offset, framesThis * Stereo));
            offset += framesThis * Stereo;
        }

        for (int i = 0; i < whole.Length; i++)
            Assert.Equal(whole[i], split[i], precision: 6);
    }

    // --- Reset & edge cases -------------------------------------------------------------------------

    [Fact]
    public void Reset_ClearsGainAndDelayState()
    {
        var limiter = MakeStereoLimiter();
        var loud = new float[SampleRate * Stereo];
        Array.Fill(loud, 2.0f);
        limiter.Process(loud);
        Assert.True(limiter.CurrentGain < 1.0);

        limiter.Reset();

        Assert.Equal(1.0, limiter.CurrentGain, precision: 6);
        // After reset the delay line is empty again: a below-ceiling impulse re-appears exactly at +L.
        int L = limiter.LatencySamples;
        var buffer = new float[(L + 32) * Stereo];
        buffer[0] = 0.5f;
        buffer[1] = 0.5f;
        limiter.Process(buffer);
        Assert.Equal(0.5f, buffer[L * Stereo], precision: 5);
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
        limiter.Process(Array.Empty<float>());
        Assert.Equal(1.0, limiter.CurrentGain, precision: 6);
    }
}
