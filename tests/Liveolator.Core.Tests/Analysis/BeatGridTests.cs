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
}
