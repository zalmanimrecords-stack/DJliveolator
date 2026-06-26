using Liveolator.Core.Beat;
using Xunit;

namespace Liveolator.Core.Tests.Beat;

public class BpmMatchTests
{
    [Fact]
    public void MatchesWhenWithinTolerance()
    {
        Assert.True(BpmMatch.AreMatched(128.0, 128.05, toleranceBpm: 0.1));
    }

    [Fact]
    public void MatchesExactlyAtTolerance()
    {
        Assert.True(BpmMatch.AreMatched(128.0, 128.1, toleranceBpm: 0.1));
    }

    [Fact]
    public void DoesNotMatchOutsideTolerance()
    {
        Assert.False(BpmMatch.AreMatched(128.0, 130.0, toleranceBpm: 0.1));
    }

    [Theory]
    [InlineData(0.0, 128.0)]
    [InlineData(128.0, 0.0)]
    [InlineData(-1.0, 128.0)]
    public void NeverMatchesWhenEitherTempoIsUnknown(double a, double b)
    {
        // A zero/negative BPM means "no tempo" (empty or un-analyzed deck) — two empty decks must never
        // read as beatmatched.
        Assert.False(BpmMatch.AreMatched(a, b, toleranceBpm: 0.1));
    }

    [Fact]
    public void NeverMatchesWhenBpmIsNotFinite()
    {
        Assert.False(BpmMatch.AreMatched(double.NaN, 128.0, toleranceBpm: 0.1));
    }
}
