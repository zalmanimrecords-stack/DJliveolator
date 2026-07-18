using System;
using System.Collections.Generic;
using Liveolator.Core.Dsp;
using Xunit;

namespace Liveolator.Core.Tests.Dsp;

/// <summary>
/// The shared STFT framing helper that every spectral analyzer runs through, so their frame
/// grids stay identical. Tested off any decode/native path.
/// </summary>
public sealed class StftTests
{
    [Fact]
    public void ValidateFrameParams_AcceptsPowerOfTwoAndInRangeHop()
    {
        // No throw.
        Stft.ValidateFrameParams(1024, 512);
        Stft.ValidateFrameParams(2, 1);
    }

    [Theory]
    [InlineData(1000)] // not a power of two
    [InlineData(1)]    // below the minimum of 2
    [InlineData(0)]
    public void ValidateFrameParams_NonPowerOfTwoFrameSize_ThrowsForFrameSize(int frameSize)
    {
        var ex = Assert.Throws<ArgumentException>(() => Stft.ValidateFrameParams(frameSize, 1));
        Assert.Equal("frameSize", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]     // hop < 1
    [InlineData(2048)]  // hop > frameSize
    public void ValidateFrameParams_HopOutOfRange_ThrowsForHop(int hop)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Stft.ValidateFrameParams(1024, hop));
        Assert.Equal("hop", ex.ParamName);
    }

    [Theory]
    [InlineData(0, 1024, 512, 0)]     // empty
    [InlineData(512, 1024, 512, 0)]   // shorter than one frame
    [InlineData(1024, 1024, 512, 1)]  // exactly one frame
    [InlineData(2048, 1024, 512, 3)]  // 1 + (2048-1024)/512
    [InlineData(1000, 1024, 512, 0)]  // just short of a frame
    public void FrameCount_MatchesHopAdvancedFraming(int sampleCount, int frameSize, int hop, int expected)
    {
        Assert.Equal(expected, Stft.FrameCount(sampleCount, frameSize, hop));
    }

    [Fact]
    public void ForEachFrame_InvokesCallbackOncePerFrame_WithSequentialIndices()
    {
        var mono = new float[2048];
        double[] window = Ones(1024);
        var indices = new List<int>();

        Stft.ForEachFrame(mono, window, hop: 512, (f, _) => indices.Add(f));

        Assert.Equal(new[] { 0, 1, 2 }, indices);
    }

    [Fact]
    public void ForEachFrame_ShorterThanOneFrame_NeverFires()
    {
        var mono = new float[100];
        bool fired = false;

        Stft.ForEachFrame(mono, Ones(1024), hop: 512, (_, _) => fired = true);

        Assert.False(fired);
    }

    [Fact]
    public void ForEachFrame_YieldsAFreshMagnitudeArrayPerFrame()
    {
        // Spectral flux retains the previous frame's magnitude, so each callback must get a distinct array.
        var mono = new float[2048];
        double[]? previous = null;

        Stft.ForEachFrame(mono, Ones(1024), hop: 512, (_, mag) =>
        {
            Assert.NotSame(previous, mag);
            previous = mag;
        });
    }

    [Fact]
    public void ForEachFrame_MatchesAHandWrittenWindowedMagnitudeLoop()
    {
        // The helper must be byte-for-byte equivalent to the framing loop it replaced.
        var mono = TestTone(frequencyHz: 60, sampleRate: 8_000, count: 4096);
        double[] window = Window.Hann(1024);
        const int hop = 512;

        var fromHelper = new List<double[]>();
        Stft.ForEachFrame(mono, window, hop, (_, mag) => fromHelper.Add(mag));

        int frames = 1 + (mono.Length - window.Length) / hop;
        Assert.Equal(frames, fromHelper.Count);

        var frame = new double[window.Length];
        for (int f = 0; f < frames; f++)
        {
            int start = f * hop;
            for (int i = 0; i < window.Length; i++)
                frame[i] = mono[start + i] * window[i];
            double[] expected = Fft.MagnitudeSpectrum(frame);
            Assert.Equal(expected, fromHelper[f]);
        }
    }

    private static double[] Ones(int n)
    {
        var w = new double[n];
        Array.Fill(w, 1.0);
        return w;
    }

    private static float[] TestTone(double frequencyHz, int sampleRate, int count)
    {
        var samples = new float[count];
        double step = 2.0 * Math.PI * frequencyHz / sampleRate;
        for (int i = 0; i < count; i++)
            samples[i] = 0.8f * (float)Math.Sin(step * i);
        return samples;
    }
}
