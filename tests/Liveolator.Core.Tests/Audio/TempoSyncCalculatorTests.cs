using Liveolator.Core.Audio.Sync;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

public class TempoSyncCalculatorTests
{
    [Fact]
    public void EqualTempo_GivesUnityRate()
    {
        Assert.Equal(1.0, TempoSyncCalculator.RateFor(128.0, 128.0), precision: 6);
    }

    [Fact]
    public void CloseTempos_GiveTheDirectRatio()
    {
        // 124 BPM follower matched to a 128 BPM leader: no octave fold needed.
        Assert.Equal(128.0 / 124.0, TempoSyncCalculator.RateFor(128.0, 124.0), precision: 6);
    }

    [Fact]
    public void HalfTempoFollower_FoldsToNearUnity()
    {
        // 70 BPM track follows a 140 BPM leader at ~1.0 (plays at its own 70, aligning every other beat).
        Assert.Equal(1.0, TempoSyncCalculator.RateFor(140.0, 70.0), precision: 6);
    }

    [Fact]
    public void DoubleTempoFollower_FoldsToNearUnity()
    {
        // 140 BPM track follows a 70 BPM leader at ~1.0 (the inverse fold).
        Assert.Equal(1.0, TempoSyncCalculator.RateFor(70.0, 140.0), precision: 6);
    }

    [Fact]
    public void FoldChoosesTheOctaveNearestUnity()
    {
        // 90 vs 130: direct ratio 130/90 = 1.444 (>√2) folds down to 0.722; the half-tempo
        // relationship (65 BPM) is the closest match, not a +44% stretch.
        double rate = TempoSyncCalculator.RateFor(130.0, 90.0);
        Assert.Equal(130.0 / 90.0 / 2.0, rate, precision: 6);
    }

    [Theory]
    [InlineData(0.0, 128.0)]
    [InlineData(128.0, 0.0)]
    [InlineData(-120.0, 128.0)]
    [InlineData(128.0, -120.0)]
    public void NonPositiveTempo_LeavesRateUnchanged(double leaderBpm, double followerBpm)
    {
        Assert.Equal(1.0, TempoSyncCalculator.RateFor(leaderBpm, followerBpm), precision: 6);
    }

    [Fact]
    public void FoldedRate_AlwaysSitsInNearestOctaveWindow()
    {
        // Whatever the relationship, the folded rate stays within [√½, √2): a sane, in-range pitch.
        foreach (double follower in new[] { 60.0, 75.0, 100.0, 145.0, 174.0 })
        {
            double rate = TempoSyncCalculator.RateFor(128.0, follower);
            Assert.InRange(rate, System.Math.Sqrt(0.5), System.Math.Sqrt(2.0));
        }
    }

    [Fact]
    public void RateWithin_GapInsideCeiling_StretchesAndReportsWithinRange()
    {
        // 120 -> 128 is +6.7%, inside a 15% sync ceiling: apply the real beatmatch rate.
        SyncRate result = TempoSyncCalculator.RateWithin(128.0, 120.0, maxStretch: 0.15);

        Assert.True(result.WithinRange);
        Assert.Equal(128.0 / 120.0, result.Rate, precision: 6);
    }

    [Fact]
    public void RateWithin_GapBeyondCeiling_ReportsOutOfRange_AndDoesNotStretch()
    {
        // 100 -> 128 folds to +28% (1.28 < √2, so no octave fold helps): beyond a 15% ceiling. Sync must
        // NOT command the out-of-range rate (the chipmunk bug); it reports out-of-range and holds unity.
        SyncRate result = TempoSyncCalculator.RateWithin(128.0, 100.0, maxStretch: 0.15);

        Assert.False(result.WithinRange);
        Assert.Equal(1.0, result.Rate, precision: 9);
    }

    [Fact]
    public void RateWithin_HalfTempoPartner_FoldsInsideCeiling()
    {
        // 70 follows 140: folds to ~1.0, trivially inside any sane ceiling.
        SyncRate result = TempoSyncCalculator.RateWithin(140.0, 70.0, maxStretch: 0.15);

        Assert.True(result.WithinRange);
        Assert.Equal(1.0, result.Rate, precision: 6);
    }

    [Theory]
    [InlineData(0.0, 120.0)]
    [InlineData(128.0, 0.0)]
    public void RateWithin_NonPositiveTempo_IsUnityAndWithinRange(double leaderBpm, double followerBpm)
    {
        SyncRate result = TempoSyncCalculator.RateWithin(leaderBpm, followerBpm, maxStretch: 0.15);

        Assert.True(result.WithinRange);
        Assert.Equal(1.0, result.Rate, precision: 9);
    }
}
