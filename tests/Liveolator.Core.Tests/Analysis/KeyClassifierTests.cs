using Liveolator.Core.Analysis.Key;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

public class KeyClassifierTests
{
    // Temperley (Kostka–Payne) profiles, independently restated here to verify the classifier.
    private static readonly double[] Major =
        { 0.748, 0.060, 0.488, 0.082, 0.670, 0.460, 0.096, 0.715, 0.104, 0.366, 0.057, 0.400 };
    private static readonly double[] Minor =
        { 0.712, 0.084, 0.474, 0.618, 0.049, 0.460, 0.105, 0.747, 0.404, 0.067, 0.133, 0.330 };

    private static double[] Rotate(double[] profile, int tonic)
    {
        var rotated = new double[12];
        for (int i = 0; i < 12; i++)
            rotated[i] = profile[(((i - tonic) % 12) + 12) % 12];
        return rotated;
    }

    [Theory]
    [InlineData(0, "8B")]   // C major
    [InlineData(2, "10B")]  // D major
    [InlineData(7, "9B")]   // G major
    public void Classify_MajorProfile_IdentifiesKeyAndCamelot(int tonic, string camelot)
    {
        MusicalKey key = new KeyClassifier().Classify(Rotate(Major, tonic));

        Assert.Equal(tonic, key.Tonic);
        Assert.Equal(KeyMode.Major, key.Mode);
        Assert.Equal(camelot, key.Camelot);
        Assert.True(key.Confidence > 0.99); // perfect match against its own profile
    }

    [Theory]
    [InlineData(9, "8A")]   // A minor
    [InlineData(0, "5A")]   // C minor
    public void Classify_MinorProfile_IdentifiesKeyAndCamelot(int tonic, string camelot)
    {
        MusicalKey key = new KeyClassifier().Classify(Rotate(Minor, tonic));

        Assert.Equal(tonic, key.Tonic);
        Assert.Equal(KeyMode.Minor, key.Mode);
        Assert.Equal(camelot, key.Camelot);
    }

    [Theory]
    [InlineData(0, 3, 7, KeyMode.Minor, "5A")]   // C Eb G  → C minor
    [InlineData(0, 4, 7, KeyMode.Major, "8B")]   // C E  G  → C major
    [InlineData(9, 0, 4, KeyMode.Minor, "8A")]   // A C  E  → A minor
    public void Classify_ATriad_ReadsItsMode(int a, int b, int c, KeyMode mode, string camelot)
    {
        var chroma = new double[12];
        chroma[a] = chroma[b] = chroma[c] = 1.0;

        MusicalKey key = new KeyClassifier().Classify(chroma);

        Assert.Equal(mode, key.Mode);
        Assert.Equal(camelot, key.Camelot);
    }

    [Fact]
    public void Classify_UndecidableChroma_DoesNotFallBackToMajor()
    {
        // A flat chroma correlates equally (at 0) with all 24 candidates. Testing major first made the
        // seated major win every such tie — a bias pointing the same way as the observed mode failures
        // (issue #5). Minor is tested first so an undecidable read is not dressed up as a major key.
        var flat = new double[12];
        Array.Fill(flat, 1.0);

        Assert.Equal(KeyMode.Minor, new KeyClassifier().Classify(flat).Mode);
    }

    [Fact]
    public void Classify_WrongChromaLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => new KeyClassifier().Classify(new double[7]));
    }
}
