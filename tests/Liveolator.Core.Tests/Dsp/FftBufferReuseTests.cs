using System;
using Liveolator.Core.Audio;
using Liveolator.Core.Dsp;
using Xunit;

namespace Liveolator.Core.Tests.Dsp;

/// <summary>
/// The realtime analysis path runs the FFT ~86 times a second on the BASS update thread, whose whole
/// budget is one output buffer (5-20 ms). Garbage produced there buys a GC pause on the audio thread,
/// which is an audible dropout — so the spectrum has to be computed without allocating.
/// </summary>
public sealed class FftBufferReuseTests
{
    private static double[] Frame(int n, int seed = 7)
    {
        var random = new Random(seed);
        var frame = new double[n];
        for (int i = 0; i < n; i++)
            frame[i] = Math.Sin(i * 0.05) + (random.NextDouble() - 0.5) * 0.1;
        return frame;
    }

    [Fact]
    public void The_buffer_overload_matches_the_allocating_one_exactly()
    {
        double[] frame = Frame(1024);

        double[] expected = Fft.MagnitudeSpectrum(frame);
        var actual = new double[expected.Length];
        Fft.MagnitudeSpectrum(frame, new double[frame.Length], new double[frame.Length], actual);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Reused_buffers_give_the_same_answer_as_fresh_ones()
    {
        // The trap this guards: Forward READS the imaginary part, and the allocating overload happened to
        // hand it a freshly zeroed array. A reused buffer still holds the previous frame unless cleared,
        // which would make every frame after the first quietly wrong.
        var re = new double[512];
        var im = new double[512];
        var scratch = new double[(512 / 2) + 1];

        double[] first = Frame(512, seed: 1);
        double[] second = Frame(512, seed: 2);

        Fft.MagnitudeSpectrum(first, re, im, scratch);   // dirties re/im with the first frame
        Fft.MagnitudeSpectrum(second, re, im, scratch);

        Assert.Equal(Fft.MagnitudeSpectrum(second), scratch);
    }

    [Fact]
    public void Repeated_analysis_allocates_only_the_arrays_it_returns()
    {
        var analyzer = new SpectrumAnalyzer(frameSize: 2048, waveformPoints: 256);
        var mono = new float[2048];
        for (int i = 0; i < mono.Length; i++)
            mono[i] = MathF.Sin(i * 0.01f);

        analyzer.Analyze(mono); // JIT and first-touch outside the measurement

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
            analyzer.Analyze(mono);
        long perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / 50;

        // What may still be allocated is the returned spectrum (1025 floats) + waveform (256 floats)
        // ≈ 5.2 KB. The FFT's own scratch — two 2048-double buffers plus the magnitude array, ~40 KB —
        // must not be in there. 8 KB leaves room for the returns and nothing like the scratch.
        Assert.True(perCall < 8_000, $"analysis allocated {perCall} bytes per call; the FFT scratch is back");
    }
}
