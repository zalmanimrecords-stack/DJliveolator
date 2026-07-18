using System;
using Liveolator.App.Features.Live.Modules;
using Xunit;

namespace Liveolator.App.Tests.Live.Modules;

/// <summary>
/// The render-time beat-phase lock (owner: "same BPM ⇒ the grids must move together"). Pure math, so it
/// verifies the alignment without a render; the visual result is checked in the render shot.
/// </summary>
public sealed class WaveformSyncScrollTests
{
    private static double BeatPhase(double progress, double duration, double firstBeat, double bpm)
    {
        double beats = (progress * duration - firstBeat) * bpm / 60.0;
        return beats - Math.Floor(beats);
    }

    [Fact]
    public void AlreadyAligned_OffsetIsZero()
    {
        double off = WaveformSyncScroll.FollowerOffset(
            masterProgress: 0.5, masterDuration: 200, masterFirstBeat: 0, masterBaseBpm: 120,
            followerProgress: 0.5, followerDuration: 200, followerFirstBeat: 0, followerBaseBpm: 120);

        Assert.Equal(0.0, off, 9);
    }

    [Fact]
    public void ShiftsTheFollowerOntoTheMastersBeat()
    {
        // Master sits on the beat (phase 0); the follower is a quarter-beat off. Applying the offset must
        // land the follower's beat phase exactly on the master's, moving it the SHORT way (a quarter beat).
        const double masterProgress = 0.5, followerProgress = 0.500625; // follower phase = 0.25 at 120bpm/200s
        Assert.Equal(0.0, BeatPhase(masterProgress, 200, 0, 120), 6);
        Assert.Equal(0.25, BeatPhase(followerProgress, 200, 0, 120), 6);

        double off = WaveformSyncScroll.FollowerOffset(
            masterProgress, 200, 0, 120, followerProgress, 200, 0, 120);

        Assert.Equal(0.0, BeatPhase(followerProgress + off, 200, 0, 120), 6); // now aligned to the master
        Assert.True(Math.Abs(off) <= 0.5 * 60.0 / 120.0 / 200.0 + 1e-12, "shift is never more than half a beat");
    }

    [Fact]
    public void WrapsToTheNearestBeat_NotTheLongWayAround()
    {
        // Follower 0.9 beat ahead of the master → shift back 0.1 beat (short way), not forward 0.9.
        const double masterProgress = 0.5, followerProgress = 0.502250; // follower phase ≈ 0.9
        double off = WaveformSyncScroll.FollowerOffset(
            masterProgress, 200, 0, 120, followerProgress, 200, 0, 120);

        Assert.Equal(0.0, BeatPhase(followerProgress + off, 200, 0, 120), 6);
        Assert.True(off > 0, "the short move onto the next beat is a small POSITIVE nudge, not a big negative one");
        Assert.True(Math.Abs(off) <= 0.5 * 60.0 / 120.0 / 200.0 + 1e-12);
    }

    [Fact]
    public void AlignsDifferentTracksOfTheSameTempo()
    {
        // Two DIFFERENT tracks (different length + downbeat anchor) at the same base BPM, off-phase. The
        // offset must still land the follower's beat on the master's — the "same BPM ⇒ move together" case.
        double off = WaveformSyncScroll.FollowerOffset(
            masterProgress: 0.30, masterDuration: 210, masterFirstBeat: 0.12, masterBaseBpm: 146,
            followerProgress: 0.55, followerDuration: 260, followerFirstBeat: 0.31, followerBaseBpm: 146);

        double masterPhase = BeatPhase(0.30, 210, 0.12, 146);
        double afterPhase = BeatPhase(0.55 + off, 260, 0.31, 146);
        Assert.Equal(masterPhase, afterPhase, 5);
    }

    [Fact]
    public void UnknownTempoOrDuration_OffsetIsZero()
    {
        Assert.Equal(0.0, WaveformSyncScroll.FollowerOffset(0.5, 200, 0, 0, 0.5, 200, 0, 120), 9);
        Assert.Equal(0.0, WaveformSyncScroll.FollowerOffset(0.5, 200, 0, 120, 0.5, 0, 0, 120), 9);
    }
}
