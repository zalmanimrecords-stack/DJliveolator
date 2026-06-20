using System;
using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.Core.Tests.Studio;

/// <summary>
/// Guards <see cref="WarpSync.SnapClipToProjectGrid"/> — the one-click "sync to project BPM": warp the
/// clip to the project tempo and shift its start so its first audible downbeat lands on the nearest
/// project grid line, as close as possible to where it was dropped. Pure math, no audio.
/// </summary>
public sealed class WarpSyncTests
{
    private const double Tol = 1e-9;

    private static StudioClip Clip(
        double timelineStart, double sourceBpm, double sourceDownbeat = 0.0,
        double sourceInSeconds = 0.0, int beatsPerBar = 4) => new(
            DeckSlot: 0,
            TrackPath: "/m/a.wav",
            TimelineStartSeconds: timelineStart,
            SourceIn: TimeSpan.FromSeconds(sourceInSeconds),
            SourceOut: null,
            SourceBpm: sourceBpm,
            SourceDownbeatSeconds: sourceDownbeat,
            SourceBeatsPerBar: beatsPerBar);

    [Fact]
    public void Sync_EnablesWarp_AndSnapsToNearestBar()
    {
        // 120 BPM project: bars at 0, 2, 4… A clip dropped at 2.1s snaps its downbeat back to 2.0.
        StudioClip clip = Clip(timelineStart: 2.1, sourceBpm: 120);

        StudioClip synced = WarpSync.SnapClipToProjectGrid(clip, projectBpm: 120, GridSnapMode.NearestDownbeat);

        Assert.True(synced.WarpEnabled);
        Assert.Equal(2.0, synced.TimelineStartSeconds, Tol);
    }

    [Fact]
    public void Sync_SnapsToTheNearestBar_NotAlwaysDown()
    {
        // 3.1s is closer to bar 4.0 (dist 0.9) than bar 2.0 (dist 1.1) — snaps up.
        StudioClip synced = WarpSync.SnapClipToProjectGrid(Clip(3.1, 120), 120, GridSnapMode.NearestDownbeat);

        Assert.Equal(4.0, synced.TimelineStartSeconds, Tol);
    }

    [Fact]
    public void Sync_WarpsTempo_SoSlowerTrackAlignsToProjectGrid()
    {
        // Source 60 BPM into a 120 BPM project: project bars are 2s. 3.1s snaps to bar 4.0.
        StudioClip synced = WarpSync.SnapClipToProjectGrid(Clip(3.1, sourceBpm: 60), 120, GridSnapMode.NearestDownbeat);

        Assert.True(synced.WarpEnabled);
        Assert.Equal(4.0, synced.TimelineStartSeconds, Tol);
    }

    [Fact]
    public void Sync_AccountsForASourceDownbeatOffset()
    {
        // Downbeat 0.3s into the source (a short pickup). Same tempo, dropped at 1.0s: its downbeat at
        // 1.3 snaps to bar 2.0, so the clip start moves to 1.7 (1.7 + 0.3 = 2.0 on the grid).
        StudioClip synced = WarpSync.SnapClipToProjectGrid(
            Clip(timelineStart: 1.0, sourceBpm: 120, sourceDownbeat: 0.3), 120, GridSnapMode.NearestDownbeat);

        Assert.Equal(1.7, synced.TimelineStartSeconds, Tol);
    }

    [Fact]
    public void Sync_NearestBeat_SnapsFinerThanNearestBar()
    {
        // 120 BPM: beats at 0.5s steps. 1.1s snaps to beat 1.0 (bar mode would jump to 2.0).
        StudioClip beat = WarpSync.SnapClipToProjectGrid(Clip(1.1, 120), 120, GridSnapMode.NearestBeat);
        StudioClip bar = WarpSync.SnapClipToProjectGrid(Clip(1.1, 120), 120, GridSnapMode.NearestDownbeat);

        Assert.Equal(1.0, beat.TimelineStartSeconds, Tol);
        Assert.Equal(2.0, bar.TimelineStartSeconds, Tol);
    }

    [Fact]
    public void Sync_UsesTheFirstDownbeatAtOrAfterTheTrimIn()
    {
        // Trimmed 2.5s into a 120 BPM source (bars at 0,2,4): the first audible downbeat is 4.0, which is
        // 1.5s into the clip. Dropped at 0, that downbeat snaps to bar 2.0 → start 0.5 (0.5 + 1.5 = 2.0).
        StudioClip synced = WarpSync.SnapClipToProjectGrid(
            Clip(timelineStart: 0.0, sourceBpm: 120, sourceDownbeat: 0.0, sourceInSeconds: 2.5),
            120, GridSnapMode.NearestDownbeat);

        Assert.Equal(0.5, synced.TimelineStartSeconds, Tol);
    }

    [Fact]
    public void Sync_WithUnknownSourceTempo_ReturnsTheClipUnchanged()
    {
        StudioClip clip = Clip(2.1, sourceBpm: 0);

        StudioClip result = WarpSync.SnapClipToProjectGrid(clip, 120, GridSnapMode.NearestDownbeat);

        Assert.False(result.WarpEnabled);
        Assert.Equal(2.1, result.TimelineStartSeconds, Tol);
    }

    [Fact]
    public void Sync_NeverPlacesTheClipBeforeTheTimelineOrigin()
    {
        // A downbeat well inside a clip dropped at 0 must not push the start negative.
        StudioClip synced = WarpSync.SnapClipToProjectGrid(
            Clip(timelineStart: 0.0, sourceBpm: 120, sourceDownbeat: 0.0, sourceInSeconds: 0.1),
            120, GridSnapMode.NearestDownbeat);

        Assert.True(synced.TimelineStartSeconds >= 0.0);
    }
}
