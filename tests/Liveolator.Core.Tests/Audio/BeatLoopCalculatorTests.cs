using System;
using Liveolator.Core.Audio.Sync;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

public class BeatLoopCalculatorTests
{
    [Fact]
    public void LengthSeconds_FourBeatsAt120Bpm_IsTwoSeconds()
    {
        // 120 BPM => 0.5 s/beat; four beats => 2 s.
        Assert.Equal(2.0, BeatLoopCalculator.LengthSeconds(4.0, 120.0), precision: 6);
    }

    [Theory]
    [InlineData(10.4, 0.0, 120.0, 10.5)]   // 0.5 s/beat: 10.4 → nearest beat 21 → 10.5 (rounds up)
    [InlineData(10.2, 0.0, 120.0, 10.0)]   // rounds down to beat 20
    [InlineData(10.0, 0.0, 120.0, 10.0)]   // already on the grid → unchanged
    [InlineData(10.3, 0.1, 120.0, 10.1)]   // non-zero anchor: (10.3-0.1)/0.5=20.4 → 20 → 0.1 + 10.0
    public void SnapToBeat_SnapsToTheNearestBeatOnTheGrid(double start, double firstBeat, double bpm, double expected)
        => Assert.Equal(expected, BeatLoopCalculator.SnapToBeat(start, firstBeat, bpm), precision: 6);

    [Fact]
    public void SnapToBeat_NonPositiveBpm_ReturnsTheInputUnchanged()
        => Assert.Equal(10.4, BeatLoopCalculator.SnapToBeat(10.4, 0.0, 0.0), precision: 6);

    [Fact]
    public void SnapToBeat_NeverReturnsNegative_WhenRoundingFallsBeforeTheTrackStart()
        => Assert.True(BeatLoopCalculator.SnapToBeat(0.05, 0.4, 120.0) >= 0.0);

    [Fact]
    public void LengthSeconds_ScalesWithTempo()
    {
        // Same four beats at a faster tempo is a shorter span.
        Assert.Equal(1.5, BeatLoopCalculator.LengthSeconds(4.0, 160.0), precision: 6);
    }

    [Fact]
    public void LengthSeconds_SupportsFractionalBeats()
    {
        // A half-beat loop at 120 BPM is a quarter second.
        Assert.Equal(0.25, BeatLoopCalculator.LengthSeconds(0.5, 120.0), precision: 6);
    }

    [Fact]
    public void Region_StartsAtInPointAndEndsAfterTheBeatLength()
    {
        LoopRegion region = BeatLoopCalculator.Region(startSeconds: 10.0, beats: 8.0, bpm: 120.0);

        Assert.Equal(10.0, region.StartSeconds, precision: 6);
        Assert.Equal(14.0, region.EndSeconds, precision: 6); // 8 beats * 0.5 s = 4 s
        Assert.Equal(4.0, region.LengthSeconds, precision: 6);
    }

    [Fact]
    public void LengthSeconds_BeatsBelowMinimum_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BeatLoopCalculator.LengthSeconds(0.0, 120.0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-120.0)]
    public void LengthSeconds_NonPositiveBpm_Throws(double bpm)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BeatLoopCalculator.LengthSeconds(4.0, bpm));
    }

    [Fact]
    public void Region_NegativeStart_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BeatLoopCalculator.Region(-1.0, 4.0, 120.0));
    }
}
