using System;
using Liveolator.Core.Dsp;
using Xunit;

namespace Liveolator.Core.Tests.Dsp;

public class LinearResamplerTests
{
    [Fact]
    public void Ctor_RejectsNonPositiveRates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LinearResampler(0, 44_100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LinearResampler(44_100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LinearResampler(-1, 44_100));
    }

    [Fact]
    public void EqualRates_IsExactPassthrough()
    {
        var r = new LinearResampler(44_100, 44_100);
        Assert.False(r.IsResampling);

        var input = new float[] { 0.1f, -0.2f, 0.3f, -0.4f, 0.5f };
        float[] output = r.Process(input);

        Assert.Equal(input, output);
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty_WithoutThrowing()
    {
        var r = new LinearResampler(48_000, 44_100);
        Assert.Empty(r.Process(ReadOnlySpan<float>.Empty));
    }

    [Fact]
    public void Downsample_ApproximatesOutputLengthByRatio()
    {
        var r = new LinearResampler(48_000, 24_000); // halve
        var input = new float[480];                  // 10 ms @ 48k → ~240 @ 24k
        for (int i = 0; i < input.Length; i++)
            input[i] = (float)Math.Sin(2.0 * Math.PI * 5.0 * i / input.Length);

        float[] output = r.Process(input);

        // Expected output count ≈ input * targetRate/sourceRate, within one sample.
        Assert.InRange(output.Length, 239, 241);
    }

    [Fact]
    public void Upsample_ApproximatesOutputLengthByRatio()
    {
        var r = new LinearResampler(24_000, 48_000); // double
        var input = new float[240];
        for (int i = 0; i < input.Length; i++)
            input[i] = (float)Math.Sin(2.0 * Math.PI * 5.0 * i / input.Length);

        float[] output = r.Process(input);

        Assert.InRange(output.Length, 479, 481);
    }

    [Fact]
    public void LinearRamp_IsPreservedUnderUpsampling()
    {
        // A straight line is reproduced exactly by linear interpolation.
        var r = new LinearResampler(10, 20);
        var input = new float[10];
        for (int i = 0; i < input.Length; i++)
            input[i] = i; // y = x

        float[] output = r.Process(input);

        // First output sample sits on the ramp; each subsequent advances by 0.5 in x.
        for (int i = 0; i < output.Length; i++)
        {
            double x = i * 0.5; // source-sample position
            Assert.Equal(x, output[i], precision: 5);
        }
    }

    [Fact]
    public void StreamingBatches_MatchOneShot_ForTheSameSignal()
    {
        // Resampling a signal split across batches must equal resampling it whole:
        // the resampler carries fractional phase + the boundary sample across calls.
        var whole = new float[1000];
        for (int i = 0; i < whole.Length; i++)
            whole[i] = (float)Math.Sin(2.0 * Math.PI * 7.0 * i / 100.0);

        var oneShot = new LinearResampler(48_000, 44_100).Process(whole);

        var streamed = new System.Collections.Generic.List<float>();
        var streaming = new LinearResampler(48_000, 44_100);
        int pos = 0;
        foreach (int batch in new[] { 137, 1, 400, 462 }) // uneven batches incl. a single sample
        {
            streamed.AddRange(streaming.Process(whole.AsSpan(pos, batch)));
            pos += batch;
        }

        Assert.Equal(oneShot.Length, streamed.Count);
        for (int i = 0; i < oneShot.Length; i++)
            Assert.Equal(oneShot[i], streamed[i], precision: 6);
    }

    [Fact]
    public void Reset_ClearsCarriedStateForReuse()
    {
        var r = new LinearResampler(48_000, 44_100);
        var input = new float[200];
        for (int i = 0; i < input.Length; i++)
            input[i] = (float)Math.Sin(2.0 * Math.PI * 3.0 * i / 50.0);

        float[] first = r.Process(input);
        r.Reset();
        float[] second = r.Process(input);

        Assert.Equal(first.Length, second.Length);
        for (int i = 0; i < first.Length; i++)
            Assert.Equal(first[i], second[i], precision: 6);
    }
}
