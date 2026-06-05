using System;
using Liveolator.Core.Audio;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

public class SpectrumAnalyzerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(1000)]
    public void Ctor_RejectsNonPowerOfTwoFrameSize(int frameSize)
    {
        Assert.Throws<ArgumentException>(() => new SpectrumAnalyzer(frameSize));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5000)] // > frameSize
    public void Ctor_RejectsOutOfRangeWaveformPoints(int waveformPoints)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpectrumAnalyzer(2048, waveformPoints));
    }

    [Fact]
    public void Analyze_RejectsWrongSizeFrame()
    {
        var analyzer = new SpectrumAnalyzer(1024);
        Assert.Throws<ArgumentException>(() => analyzer.Analyze(new float[512]));
    }

    [Fact]
    public void Analyze_ProducesExpectedLengths()
    {
        var analyzer = new SpectrumAnalyzer(frameSize: 1024, waveformPoints: 128);

        var (spectrum, waveform) = analyzer.Analyze(new float[1024]);

        Assert.Equal(1024 / 2 + 1, spectrum.Length);
        Assert.Equal(1024 / 2 + 1, analyzer.SpectrumBins);
        Assert.Equal(128, waveform.Length);
    }

    [Fact]
    public void Analyze_SineWave_PeaksNearItsBin()
    {
        const int frameSize = 1024;
        const int k = 16; // cycles per frame → spectral peak expected at bin k
        var analyzer = new SpectrumAnalyzer(frameSize);

        var frame = new float[frameSize];
        for (int i = 0; i < frameSize; i++)
            frame[i] = (float)Math.Sin(2.0 * Math.PI * k * i / frameSize);

        var (spectrum, _) = analyzer.Analyze(frame);

        int peakBin = 0;
        for (int i = 1; i < spectrum.Length; i++)
            if (spectrum[i] > spectrum[peakBin]) peakBin = i;

        // Hann windowing spreads energy across adjacent bins; the peak stays within ±1 of k.
        Assert.InRange(peakBin, k - 1, k + 1);
    }

    [Fact]
    public void Analyze_Waveform_AveragesDcOffset()
    {
        var analyzer = new SpectrumAnalyzer(frameSize: 256, waveformPoints: 8);

        var frame = new float[256];
        Array.Fill(frame, 0.5f);

        var (_, waveform) = analyzer.Analyze(frame);

        Assert.All(waveform, v => Assert.Equal(0.5f, v, precision: 5));
    }
}
