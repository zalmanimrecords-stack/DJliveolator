using Liveolator.Core.Analysis.Key;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

public class KeyClassifierTests
{
    // Krumhansl–Kessler profiles (independently restated here to verify the classifier).
    private static readonly double[] Major =
        { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
    private static readonly double[] Minor =
        { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };

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

    [Fact]
    public void Classify_WrongChromaLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => new KeyClassifier().Classify(new double[7]));
    }
}
