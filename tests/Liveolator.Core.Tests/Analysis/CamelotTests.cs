using Liveolator.Core.Analysis.Key;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

public class CamelotTests
{
    [Theory]
    [InlineData(0, KeyMode.Major, "8B")]   // C major
    [InlineData(9, KeyMode.Minor, "8A")]   // A minor
    [InlineData(4, KeyMode.Major, "12B")]  // E major
    [InlineData(11, KeyMode.Major, "1B")]  // B major
    [InlineData(8, KeyMode.Minor, "1A")]   // G# minor
    public void Code_MapsKeyToCamelot(int tonic, KeyMode mode, string expected)
    {
        Assert.Equal(expected, Camelot.Code(tonic, mode));
    }

    [Theory]
    [InlineData("8A", "8A")]   // same key
    [InlineData("8A", "8B")]   // relative major/minor
    [InlineData("8A", "9A")]   // adjacent +1
    [InlineData("8A", "7A")]   // adjacent -1
    [InlineData("12B", "1B")]  // adjacent wrap 12→1
    public void IsCompatible_TrueForHarmonicMoves(string seed, string other)
    {
        Assert.True(Camelot.IsCompatible(seed, other));
    }

    [Theory]
    [InlineData("8A", "10A")]  // two steps away
    [InlineData("8A", "3B")]   // unrelated
    public void IsCompatible_FalseForIncompatibleMoves(string seed, string other)
    {
        Assert.False(Camelot.IsCompatible(seed, other));
    }
}
