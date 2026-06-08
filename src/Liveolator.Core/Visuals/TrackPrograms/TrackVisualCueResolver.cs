namespace Liveolator.Core.Visuals.TrackPrograms;

/// <summary>Pure timeline math shared by preview, coordinator, and tests.</summary>
public static class TrackVisualCueResolver
{
    /// <summary>Finds the cue active at an original-track position, or null in an uncovered gap.</summary>
    public static TrackVisualCue? Resolve(TrackVisualProgram program, TimeSpan trackPosition)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (trackPosition < TimeSpan.Zero)
            return null;

        for (int i = 0; i < program.Cues.Count; i++)
        {
            TrackVisualCue cue = program.Cues[i];
            if (trackPosition < cue.StartAt)
                return null;

            TimeSpan? effectiveEnd = cue.EndAt
                ?? (i + 1 < program.Cues.Count ? program.Cues[i + 1].StartAt : program.Track.Duration);
            if (effectiveEnd is null || trackPosition < effectiveEnd)
                return cue;
        }

        return null;
    }

    /// <summary>
    /// Maps original-track time to source-media time. Loop wraps within SourceIn/SourceOut; Once
    /// clamps at SourceOut so the decoder can hold the final frame.
    /// </summary>
    public static TimeSpan ResolveSourceTime(TrackVisualCue cue, TimeSpan trackPosition)
    {
        ArgumentNullException.ThrowIfNull(cue);

        TimeSpan elapsed = trackPosition <= cue.StartAt
            ? TimeSpan.Zero
            : trackPosition - cue.StartAt;
        TimeSpan sourceStart = cue.SourceIn ?? TimeSpan.Zero;
        if (cue.SourceOut is not { } sourceEnd)
            return sourceStart + elapsed;

        TimeSpan range = sourceEnd - sourceStart;
        if (cue.Playback == VisualPlaybackMode.Once)
            return sourceStart + TimeSpan.FromTicks(Math.Min(elapsed.Ticks, range.Ticks));

        long wrappedTicks = elapsed.Ticks % range.Ticks;
        return sourceStart + TimeSpan.FromTicks(wrappedTicks);
    }
}
