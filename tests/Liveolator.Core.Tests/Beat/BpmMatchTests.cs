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

    [Theory]
    [InlineData(140.0, 70.0)]   // exact half-time
    [InlineData(70.0, 140.0)]   // exact double-time (order-independent)
    [InlineData(140.0, 35.0)]   // quarter-time (two octaves)
    [InlineData(140.0, 70.05)]  // half-time, within the folded tolerance
    public void MatchesAtOctave(double a, double b)
    {
        // A half/double-time SYNC is a genuine beatmatch — the decks' kicks align every other beat — so the
        // "matched" highlight must light even though the raw counters read ~2x apart (doc 11 fold).
        Assert.True(BpmMatch.AreMatched(a, b, toleranceBpm: 0.1));
    }

    [Theory]
    [InlineData(140.0, 71.0)]    // near half but off the grid — not locked
    [InlineData(128.0, 96.0)]    // a 4:3 ratio, not an octave relationship
    public void DoesNotMatchWhenNeitherUnisonNorOctave(double a, double b)
    {
        Assert.False(BpmMatch.AreMatched(a, b, toleranceBpm: 0.1));
    }

    // --- OctaveFactor: the power-of-two relationship the UI tags a half/double-time lock with ---

    [Theory]
    [InlineData(128.0, 128.0)]  // unison
    [InlineData(128.0, 128.05)] // unison, within the fold window
    public void OctaveFactor_IsUnity_AtUnison(double bpm, double reference)
    {
        Assert.Equal(1.0, BpmMatch.OctaveFactor(bpm, reference), precision: 9);
    }

    [Fact]
    public void OctaveFactor_IsHalf_WhenDeckRunsAtHalfTime()
    {
        // The 70-BPM deck against a 140-BPM partner is at half-time → "½×".
        Assert.Equal(0.5, BpmMatch.OctaveFactor(70.0, 140.0), precision: 9);
    }

    [Fact]
    public void OctaveFactor_IsDouble_WhenDeckRunsAtDoubleTime()
    {
        // The 140-BPM deck against a 70-BPM partner is at double-time → "2×".
        Assert.Equal(2.0, BpmMatch.OctaveFactor(140.0, 70.0), precision: 9);
    }

    [Theory]
    [InlineData(35.0, 140.0, 0.25)] // two octaves down
    [InlineData(140.0, 35.0, 4.0)]  // two octaves up
    public void OctaveFactor_FoldsDeeperOctaves(double bpm, double reference, double expected)
    {
        Assert.Equal(expected, BpmMatch.OctaveFactor(bpm, reference), precision: 9);
    }

    [Theory]
    [InlineData(0.0, 128.0)]
    [InlineData(128.0, 0.0)]
    [InlineData(double.NaN, 128.0)]
    public void OctaveFactor_IsUnity_WhenEitherTempoIsUnknown(double bpm, double reference)
    {
        // No tempo → treat as unison so the caller shows no octave tag rather than a bogus factor.
        Assert.Equal(1.0, BpmMatch.OctaveFactor(bpm, reference), precision: 9);
    }
}
