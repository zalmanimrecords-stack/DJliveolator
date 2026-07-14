using Liveolator.Core.Analysis.Bpm;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

public sealed class KickGridSnapTests
{
    private const double Bpm = 120.0;          // beat = 0.5 s
    private static readonly double[] Kicks = { 0.02, 0.52, 1.02, 1.52, 2.02 }; // kicks 20 ms off the round grid

    [Fact]
    public void NearestKickAnchor_FoldsTheNearestKickIntoOneBeat()
    {
        // Playhead near the 1.52 s kick → anchor is that kick folded into a beat: 1.52 % 0.5 = 0.02.
        double anchor = KickGridSnap.NearestKickAnchor(Kicks, playheadSeconds: 1.49, Bpm);

        Assert.Equal(0.02, anchor, 4);
    }

    [Fact]
    public void NearestKickAnchor_PicksTheClosestKick_NotTheRawPlayhead()
    {
        // Playhead at 0.80 s sits between kicks 0.52 and 1.02; 1.02 is closer (0.22 vs 0.28) → 1.02 % 0.5.
        double anchor = KickGridSnap.NearestKickAnchor(Kicks, playheadSeconds: 0.80, Bpm);

        Assert.Equal(0.02, anchor, 4);
    }

    [Fact]
    public void NearestKickAnchor_AnchorIsWithinOneBeat()
    {
        double anchor = KickGridSnap.NearestKickAnchor(new[] { 7.37 }, playheadSeconds: 7.0, Bpm);

        Assert.InRange(anchor, 0.0, 60.0 / Bpm);
        Assert.Equal(7.37 % 0.5, anchor, 4);
    }

    [Fact]
    public void NearestKickAnchor_NoKicksOrNoTempo_ReturnsFallback()
    {
        Assert.Equal(0.31, KickGridSnap.NearestKickAnchor(System.Array.Empty<double>(), 5.0, Bpm, fallbackAnchor: 0.31), 6);
        Assert.Equal(0.31, KickGridSnap.NearestKickAnchor(Kicks, 5.0, bpm: 0.0, fallbackAnchor: 0.31), 6);
    }
}
