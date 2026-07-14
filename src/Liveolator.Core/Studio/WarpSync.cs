namespace Liveolator.Core.Studio;

/// <summary>Which project grid line a clip's downbeat snaps to when syncing.</summary>
public enum GridSnapMode
{
    /// <summary>Snap to the nearest project beat (finer; used as the low-confidence fallback).</summary>
    NearestBeat,

    /// <summary>Snap to the nearest project bar/downbeat — phrase-locked, the default for set building.</summary>
    NearestDownbeat,
}

/// <summary>
/// Pure "sync a clip to the project grid" math (the one-click right-click action). Warps the clip to the
/// project tempo (keylock preserves pitch) and shifts its start so the clip's first audible downbeat lands
/// on the nearest project grid line — staying as close as possible to where the user dropped it, while
/// falling exactly in phase with the other tracks. No audio here: it only rewrites placement + warp on the
/// immutable <see cref="StudioClip"/>, so it is fully unit-testable and never touches the realtime path.
/// </summary>
public static class WarpSync
{
    /// <summary>
    /// Returns a copy of <paramref name="clip"/> warped to <paramref name="projectBpm"/> with its start
    /// shifted so its first audible downbeat sits on the nearest project grid line. Returns the clip
    /// unchanged when it cannot sync (source or project tempo unknown). The project's beat-1 is the
    /// timeline origin (t = 0), so the grid lines are integer multiples of the project beat/bar from 0.
    /// </summary>
    public static StudioClip SnapClipToProjectGrid(StudioClip clip, double projectBpm, GridSnapMode mode)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (clip.SourceBpm <= 0.0 || projectBpm <= 0.0)
            return clip;

        double factor = projectBpm / clip.SourceBpm; // warp read-rate; > 1 plays faster
        int beatsPerBar = clip.SourceBeatsPerBar > 0 ? clip.SourceBeatsPerBar : 4;
        double sourceBarSeconds = beatsPerBar * 60.0 / clip.SourceBpm;
        double sourceIn = clip.SourceIn.TotalSeconds;

        // The first source downbeat at or after the trim-in point — the first bar the listener hears.
        double barsAfterIn = Math.Ceiling((sourceIn - clip.SourceDownbeatSeconds) / sourceBarSeconds);
        double firstAudibleSourceDownbeat = clip.SourceDownbeatSeconds + barsAfterIn * sourceBarSeconds;

        // How far into the clip that downbeat sits on the timeline (source seconds compress by the factor).
        double downbeatOffsetInClip = (firstAudibleSourceDownbeat - sourceIn) / factor;
        double currentDownbeatOnTimeline = clip.TimelineStartSeconds + downbeatOffsetInClip;

        // Snap that downbeat to the nearest project grid line (mirrors BeatGrid.NearestDownbeatTo with the
        // project origin at 0). NearestDownbeat = bar-locked (phrase match); NearestBeat = finer fallback.
        double projectBeatSeconds = 60.0 / projectBpm;
        double grid = mode == GridSnapMode.NearestDownbeat ? projectBeatSeconds * beatsPerBar : projectBeatSeconds;
        double snappedDownbeat = Math.Round(currentDownbeatOnTimeline / grid) * grid;

        // Back-solve the clip start so the snapped downbeat is honoured; clamp to the timeline origin.
        double newStart = Math.Max(0.0, snappedDownbeat - downbeatOffsetInClip);
        return clip with { WarpEnabled = true, TimelineStartSeconds = newStart };
    }
}
