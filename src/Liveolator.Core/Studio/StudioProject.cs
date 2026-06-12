namespace Liveolator.Core.Studio;

/// <summary>
/// A STUDIO arrangement (a "basic DAW" project): the clips placed across the deck lanes and the
/// automation curves over them, at a project tempo. The pure, serializable model the timeline UI
/// edits, the live transport plays, and the offline renderer mixes down. Replaces the earlier
/// harmonic set-planner model — this captures a full arrangement, not just an ordered track list.
/// </summary>
public sealed record StudioProject(
    string Name,
    double Bpm,
    IReadOnlyList<StudioClip> Clips,
    IReadOnlyList<AutomationLane> Automation)
{
    /// <summary>Default project tempo when none is set (a neutral 4/4 reference).</summary>
    public const double DefaultBpm = 120.0;

    /// <summary>An empty project with the given name.</summary>
    public static StudioProject Empty(string name)
        => new(name, DefaultBpm, Array.Empty<StudioClip>(), Array.Empty<AutomationLane>());

    /// <summary>The latest timeline position any clip with a known length reaches (0 when empty/open).</summary>
    public double DurationSeconds
        => Clips.Count == 0 ? 0 : Clips.Max(c => c.TimelineEndSeconds ?? c.TimelineStartSeconds);
}
