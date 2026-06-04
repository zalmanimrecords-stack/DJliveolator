using Liveolator.Core.Analysis.Key;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

public class ChromaExtractorTests
{
    [Theory]
    [InlineData(440.0, 9)]   // A4 → pitch class A (9)
    [InlineData(261.63, 0)]  // C4 → pitch class C (0)
    [InlineData(329.63, 4)]  // E4 → pitch class E (4)
    public void Compute_PureTone_PeaksAtItsPitchClass(double freq, int expectedPitchClass)
    {
        const int sr = 44100;
        float[] tone = TestSignals.Sine(freq, sr, seconds: 1.0);

        double[] chroma = new ChromaExtractor().Compute(tone, sr);

        int peak = 0;
        for (int i = 1; i < 12; i++)
            if (chroma[i] > chroma[peak]) peak = i;

        Assert.Equal(expectedPitchClass, peak);
    }

    [Fact]
    public void Compute_NormalizesToUnitSum()
    {
        float[] tone = TestSignals.Sine(440.0, 44100, seconds: 1.0);
        double[] chroma = new ChromaExtractor().Compute(tone, 44100);
        Assert.Equal(1.0, chroma.Sum(), precision: 6);
    }

    [Fact]
    public void Compute_Silence_ReturnsZeros()
    {
        double[] chroma = new ChromaExtractor().Compute(new float[44100], 44100);
        Assert.All(chroma, v => Assert.Equal(0.0, v));
    }
}
