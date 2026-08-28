using Liveolator.Core.Analysis.Bpm;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

public sealed class BeatGridTests
{
    // 120 BPM → beat = 0.5 s, bar (4/4) = 2.0 s, downbeat anchored 0.1 s in.
    private static BeatGrid Grid(double downbeat = 0.1) => new(Bpm: 120.0, DownbeatSeconds: downbeat, BeatsPerBar: 4, Confidence: 0.9);

    [Fact]
    public void BeatAndBarSeconds_FollowTempoAndMeter()
    {
        BeatGrid grid = Grid();
        Assert.Equal(0.5, grid.BeatSeconds, 6);
        Assert.Equal(2.0, grid.BarSeconds, 6);
    }

    [Fact]
    public void BeatPhase_IsZeroOnTheDownbeatAndOnEveryBeat()
    {
        BeatGrid grid = Grid(downbeat: 0.1);
        Assert.Equal(0.0, grid.BeatPhaseAt(0.1), 4);   // the downbeat itself
        Assert.Equal(0.0, grid.BeatPhaseAt(0.6), 4);   // one beat later
        Assert.Equal(0.5, grid.BeatPhaseAt(0.35), 4);  // halfway between beats
    }

    [Fact]
    public void BarPhase_WrapsOverFourBeats()
    {
        BeatGrid grid = Grid(downbeat: 0.0);
        Assert.Equal(0.0, grid.BarPhaseAt(0.0), 4);   // downbeat
        Assert.Equal(0.25, grid.BarPhaseAt(0.5), 4);  // beat 2 of 4
        Assert.Equal(0.0, grid.BarPhaseAt(2.0), 4);   // next downbeat
    }

    [Fact]
    public void Phase_IsStableBeforeTheAnchor_NoNegativeWraparound()
    {
        // Times before the downbeat must still yield a clean [0,1) phase, not a negative.
        BeatGrid grid = Grid(downbeat: 0.1);
        double phase = grid.BeatPhaseAt(0.0);
        Assert.InRange(phase, 0.0, 1.0);
        Assert.Equal(0.8, phase, 4); // 0.1 s before a beat = 0.4 s into the previous beat = phase 0.8
    }

    [Fact]
    public void NearestDownbeat_SnapsToTheClosestBarBoundary()
    {
        BeatGrid grid = Grid(downbeat: 0.0);
        Assert.Equal(2.0, grid.NearestDownbeatTo(2.3), 4);  // just past the 2nd downbeat
        Assert.Equal(4.0, grid.NearestDownbeatTo(3.2), 4);  // closer to the 3rd
    }

    [Fact]
    public void None_HasNoTempo_AndPhasesAreZero()
    {
        Assert.False(BeatGrid.None.HasTempo);
        Assert.Equal(0.0, BeatGrid.None.BeatPhaseAt(1.23));
        Assert.Equal(0.0, BeatGrid.None.BarPhaseAt(1.23));
    }

    [Fact]
    public void FromBpmResult_CarriesDownbeatAndMeter()
    {
        var bpm = new BpmResult(128.0, Confidence: 0.8, FirstBeatSeconds: 0.05)
        {
            DownbeatSeconds = 0.3,
            BeatsPerBar = 4,
            DownbeatConfidence = 0.6,
        };

        BeatGrid grid = BeatGrid.FromBpmResult(bpm);

        Assert.Equal(128.0, grid.Bpm, 6);
        Assert.Equal(0.3, grid.DownbeatSeconds, 6);
        Assert.Equal(4, grid.BeatsPerBar);
        Assert.Equal(0.6, grid.Confidence, 6);
    }

    // ---- manual grid nudge ----------------------------------------------------------------------

    [Fact]
    public void FromBpmResult_AppliesTheDownbeatOffset_ToTheAnchor()
    {
        // The DJ's grid nudge: the detector found the downbeat 20 ms early, so the correction rides
        // alongside the detected value instead of overwriting it. Analysis stays reproducible; the grid moves.
        var bpm = new BpmResult(140.0, Confidence: 0.8)
        {
            DownbeatSeconds = 1.0,
            DownbeatOffsetSeconds = 0.020,
        };

        BeatGrid grid = BeatGrid.FromBpmResult(bpm);

        Assert.Equal(1.020, grid.DownbeatSeconds, 6);
    }

    [Fact]
    public void FromBpmResult_WithNoOffset_IsUnchanged()
    {
        // Back-compat: every catalog written before the offset existed must grid exactly as it did.
        var bpm = new BpmResult(140.0, Confidence: 0.8) { DownbeatSeconds = 1.0 };

        Assert.Equal(1.0, BeatGrid.FromBpmResult(bpm).DownbeatSeconds, 6);
    }

    [Fact]
    public void DownbeatOffset_ShiftsEveryDownbeat_ByExactlyTheOffset()
    {
        // 140 BPM -> bar = 1.714285... s. Nudging the anchor must move the whole grid rigidly, not just
        // the first bar, or a correction that fixes the intro drifts back out by the drop.
        var bpm = new BpmResult(140.0, Confidence: 0.8) { DownbeatSeconds = 1.0 };
        var nudged = bpm with { DownbeatOffsetSeconds = 0.020 };

        BeatGrid plain = BeatGrid.FromBpmResult(bpm);
        BeatGrid shifted = BeatGrid.FromBpmResult(nudged);

        foreach (double probe in new[] { 5.0, 60.0, 300.0 })
            Assert.Equal(plain.NearestDownbeatTo(probe) + 0.020, shifted.NearestDownbeatTo(probe + 0.020), 6);
    }

    [Fact]
    public void DownbeatOffset_MayBeNegative_ToPullTheGridEarlier()
    {
        var bpm = new BpmResult(140.0, Confidence: 0.8)
        {
            DownbeatSeconds = 1.0,
            DownbeatOffsetSeconds = -0.012,
        };

        Assert.Equal(0.988, BeatGrid.FromBpmResult(bpm).DownbeatSeconds, 6);
    }
}
