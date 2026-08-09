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
    public void Compute_LoudSubBass_DoesNotDecideThePitchClass()
    {
        // The real failure behind issue #5. At a 4096-sample frame the FFT bins are ~10.8 Hz apart, so
        // nothing below ~181 Hz can be told from its neighbouring semitone — every low bin dumps its
        // energy on whichever pitch class its centre rounds to, the same one for every track. In
        // electronic music that region carries most of the magnitude, so a sub an octave-and-a-half
        // below the harmony was outvoting the harmony itself.
        const int sr = 44100;
        float[] signal = TestSignals.Chord(
            new[] { (55.0, 1.0), (261.63, 0.3), (329.63, 0.1), (392.0, 0.1) }, sr, seconds: 2.0);

        double[] chroma = new ChromaExtractor().Compute(signal, sr);

        int peak = 0;
        for (int i = 1; i < 12; i++)
            if (chroma[i] > chroma[peak]) peak = i;
        Assert.Equal(0, peak);          // C, the root of the audible triad
        // A is where the 55 Hz sub would have landed; only its window leakage reaches the chroma now.
        Assert.True(chroma[9] < 0.01, $"the sub's pitch class still holds {chroma[9]:P1} of the chroma");
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
