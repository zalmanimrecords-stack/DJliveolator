namespace Liveolator.Core.Studio;

/// <summary>
/// Cuts a time window out of an arrangement as a standalone <see cref="StudioProject"/> starting at zero.
/// <para>Rendering a whole DJ set is not viable: the offline renderer holds the entire master and every
/// decoded source in memory at once, so an hour-long mix runs into gigabytes. Slicing lets a caller render
/// only the parts worth listening to — the transitions — decoding just the two records involved in each.</para>
/// <para>Pure timeline math: trims each clip to the window and rebases the automation, so the slice sounds
/// exactly like that stretch of the original.</para>
/// </summary>
public static class ProjectSlice
{
    /// <summary>
    /// The stretch of <paramref name="project"/> from <paramref name="startSeconds"/> to
    /// <paramref name="endSeconds"/>, rebased so it begins at zero. Clips outside the window are dropped;
    /// clips crossing an edge are trimmed. An empty or inverted window yields an empty project.
    /// </summary>
    public static StudioProject Extract(StudioProject project, double startSeconds, double endSeconds, string name)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        double start = Math.Max(0.0, startSeconds);
        if (endSeconds <= start)
            return StudioProject.Empty(name);

        var clips = new List<StudioClip>();
        foreach (StudioClip clip in project.Clips)
        {
            double factor = WarpMath.WarpFactorAt(clip, project.EffectiveTempo, project.Bpm, clip.TimelineStartSeconds);
            double clipStart = clip.TimelineStartSeconds;
            double clipEnd = clip.SourceDuration is { } duration
                ? clipStart + WarpMath.WarpedTimelineSeconds(duration.TotalSeconds, factor)
                : double.PositiveInfinity;
            if (clipEnd <= start || clipStart >= endSeconds)
                continue;

            // Timeline seconds cut from the head convert to source seconds at the clip's own read rate.
            double headTrim = Math.Max(0.0, start - clipStart);
            double sourceIn = clip.SourceIn.TotalSeconds + (headTrim * factor);
            double sourceOut = clip.SourceIn.TotalSeconds + ((Math.Min(clipEnd, endSeconds) - clipStart) * factor);

            clips.Add(clip with
            {
                TimelineStartSeconds = Math.Max(0.0, clipStart - start),
                SourceIn = TimeSpan.FromSeconds(sourceIn),
                SourceOut = TimeSpan.FromSeconds(sourceOut),
            });
        }

        return new StudioProject(name, project.Bpm, clips, Rebase(project.Automation, start, endSeconds), project.Tempo);
    }

    // Each lane keeps the keyframes inside the window, bracketed by its value at both edges. A lane holds
    // flat outside its keyframes, so without the brackets a fade starting before the window would open at
    // the wrong level, and one still ramping at the window end would flatten out mid-slope.
    private static IReadOnlyList<AutomationLane> Rebase(
        IReadOnlyList<AutomationLane> lanes,
        double start,
        double end)
    {
        var rebased = new List<AutomationLane>(lanes.Count);
        foreach (AutomationLane lane in lanes)
        {
            if (lane.Keyframes.Count == 0)
                continue;

            var keyframes = new List<AutomationKeyframe> { new(0.0, lane.ValueAt(start)) };
            keyframes.AddRange(lane.Keyframes
                .Where(k => k.TimeSeconds > start && k.TimeSeconds < end)
                .Select(k => k with { TimeSeconds = k.TimeSeconds - start }));
            keyframes.Add(new AutomationKeyframe(end - start, lane.ValueAt(end)));

            rebased.Add(lane with { Keyframes = keyframes });
        }

        return rebased;
    }
}
