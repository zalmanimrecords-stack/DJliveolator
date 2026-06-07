using Liveolator.Core.Audio.Sync;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

public class PhaseAlignmentCalculatorTests
{
    [Fact]
    public void BeatDistance_OnFirstBeat_IsZero()
    {
        // Playhead exactly on the first-beat anchor sits on a beat boundary.
        Assert.Equal(0.0, PhaseAlignmentCalculator.BeatDistance(0.5, 0.5, 120.0), precision: 6);
    }

    [Fact]
    public void BeatDistance_HalfwayThroughABeat_IsPointFive()
    {
        // 120 BPM => 0.5 s per beat; a quarter-second past the anchor is half a beat in.
        Assert.Equal(0.5, PhaseAlignmentCalculator.BeatDistance(0.25, 0.0, 120.0), precision: 6);
    }

    [Fact]
    public void BeatDistance_WrapsAcrossWholeBeats()
    {
        // 2.75 beats past the anchor => distance 0.75.
        double position = 0.0 + (2.75 * (60.0 / 120.0));
        Assert.Equal(0.75, PhaseAlignmentCalculator.BeatDistance(position, 0.0, 120.0), precision: 6);
    }

    [Fact]
    public void BeatDistance_BeforeAnchor_StaysInUnitInterval()
    {
        // A playhead before the first beat must still report a phase in [0,1).
        double d = PhaseAlignmentCalculator.BeatDistance(0.1, 0.3, 120.0);
        Assert.InRange(d, 0.0, 1.0);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-120.0)]
    public void BeatDistance_NonPositiveBpm_IsZero(double bpm)
    {
        Assert.Equal(0.0, PhaseAlignmentCalculator.BeatDistance(1.0, 0.0, bpm), precision: 6);
    }

    [Fact]
    public void PhaseNudge_AlreadyAligned_IsZero()
    {
        var deck = new DeckPhase(PositionSeconds: 1.0, FirstBeatSeconds: 0.0, Bpm: 120.0);
        Assert.Equal(0.0, PhaseAlignmentCalculator.PhaseNudgeSeconds(deck, deck), precision: 6);
    }

    [Fact]
    public void PhaseNudge_FollowerBehind_MovesForwardToLeaderPhase()
    {
        // Leader is half a beat into its grid; follower is exactly on a beat. Shortest snap = +0.25 s
        // (forward half a 120-BPM beat).
        var leader = new DeckPhase(PositionSeconds: 0.25, FirstBeatSeconds: 0.0, Bpm: 120.0);
        var follower = new DeckPhase(PositionSeconds: 0.0, FirstBeatSeconds: 0.0, Bpm: 120.0);

        double nudge = PhaseAlignmentCalculator.PhaseNudgeSeconds(follower, leader);

        Assert.Equal(0.25, nudge, precision: 6);
    }

    [Fact]
    public void PhaseNudge_ChoosesShortestDirection_BackwardWhenCloser()
    {
        // Follower 0.4 beats ahead of the leader's beat => snapping back 0.4 beats is shorter than
        // forward 0.6. At 120 BPM a beat is 0.5 s, so the nudge is -0.2 s.
        var leader = new DeckPhase(PositionSeconds: 0.0, FirstBeatSeconds: 0.0, Bpm: 120.0);
        var follower = new DeckPhase(PositionSeconds: 0.2, FirstBeatSeconds: 0.0, Bpm: 120.0);

        double nudge = PhaseAlignmentCalculator.PhaseNudgeSeconds(follower, leader);

        Assert.Equal(-0.2, nudge, precision: 6);
    }

    [Fact]
    public void PhaseNudge_NeverExceedsHalfAFollowerBeat()
    {
        // Any phase relationship resolves to a correction within ±half a beat (the shortest snap).
        double half = 0.5 * (60.0 / 100.0);
        foreach (double leaderPos in new[] { 0.0, 0.13, 0.31, 0.49, 0.6, 0.95 })
        {
            var leader = new DeckPhase(leaderPos, 0.0, 100.0);
            var follower = new DeckPhase(0.07, 0.0, 100.0);
            double nudge = PhaseAlignmentCalculator.PhaseNudgeSeconds(follower, leader);
            Assert.InRange(nudge, -half - 1e-9, half + 1e-9);
        }
    }

    [Fact]
    public void PhaseNudge_HonorsEachDeckFirstBeatAnchor()
    {
        // Decks share tempo but have different downbeat offsets; alignment is measured from each anchor.
        var leader = new DeckPhase(PositionSeconds: 1.0, FirstBeatSeconds: 0.0, Bpm: 120.0);   // on a beat
        var follower = new DeckPhase(PositionSeconds: 1.0, FirstBeatSeconds: 0.1, Bpm: 120.0); // 0.9 s past its anchor

        // Follower distance = (1.0-0.1)/0.5 = 1.8 -> 0.8; leader = 0.0. Error = -0.8 -> wraps to +0.2 beats
        // forward = +0.1 s.
        double nudge = PhaseAlignmentCalculator.PhaseNudgeSeconds(follower, leader);

        Assert.Equal(0.1, nudge, precision: 6);
    }

    [Theory]
    [InlineData(0.0, 120.0)]
    [InlineData(120.0, 0.0)]
    public void PhaseNudge_NonPositiveTempo_IsZero(double followerBpm, double leaderBpm)
    {
        var follower = new DeckPhase(0.3, 0.0, followerBpm);
        var leader = new DeckPhase(0.1, 0.0, leaderBpm);
        Assert.Equal(0.0, PhaseAlignmentCalculator.PhaseNudgeSeconds(follower, leader), precision: 6);
    }

    [Fact]
    public void BeatPhaseError_FollowerBehind_IsPositive()
    {
        // Leader 0.1 beat into its grid, follower on a beat => follower is behind by +0.1 beat.
        var leader = new DeckPhase(PositionSeconds: 0.1 * (60.0 / 120.0), FirstBeatSeconds: 0.0, Bpm: 120.0);
        var follower = new DeckPhase(PositionSeconds: 0.0, FirstBeatSeconds: 0.0, Bpm: 120.0);

        Assert.Equal(0.1, PhaseAlignmentCalculator.BeatPhaseError(follower, leader), precision: 6);
    }

    [Fact]
    public void BeatPhaseError_FollowerAhead_IsNegative()
    {
        var leader = new DeckPhase(PositionSeconds: 0.0, FirstBeatSeconds: 0.0, Bpm: 120.0);
        var follower = new DeckPhase(PositionSeconds: 0.1 * (60.0 / 120.0), FirstBeatSeconds: 0.0, Bpm: 120.0);

        Assert.Equal(-0.1, PhaseAlignmentCalculator.BeatPhaseError(follower, leader), precision: 6);
    }

    [Fact]
    public void BeatPhaseError_WrapsToNearestHalfBeat()
    {
        // 0.6-beat raw error is shorter measured as -0.4: any relationship resolves within ±0.5 beat.
        var leader = new DeckPhase(PositionSeconds: 0.6 * (60.0 / 120.0), FirstBeatSeconds: 0.0, Bpm: 120.0);
        var follower = new DeckPhase(PositionSeconds: 0.0, FirstBeatSeconds: 0.0, Bpm: 120.0);

        double error = PhaseAlignmentCalculator.BeatPhaseError(follower, leader);

        Assert.Equal(-0.4, error, precision: 6);
        Assert.InRange(error, -0.5, 0.5);
    }

    [Fact]
    public void PhaseNudge_EqualsBeatPhaseError_TimesBeatSeconds()
    {
        // The seconds nudge is just the wrapped beat error scaled by the follower's beat length — the two
        // helpers share one definition.
        var leader = new DeckPhase(PositionSeconds: 0.31, FirstBeatSeconds: 0.0, Bpm: 128.0);
        var follower = new DeckPhase(PositionSeconds: 0.07, FirstBeatSeconds: 0.0, Bpm: 128.0);

        double errorBeats = PhaseAlignmentCalculator.BeatPhaseError(follower, leader);
        double nudge = PhaseAlignmentCalculator.PhaseNudgeSeconds(follower, leader);

        Assert.Equal(errorBeats * (60.0 / 128.0), nudge, precision: 9);
    }
}
