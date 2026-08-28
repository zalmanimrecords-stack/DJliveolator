namespace Liveolator.Core.Studio.Set;

/// <summary>
/// The result of building a set: the arrangement itself plus everything needed to judge it without
/// listening. Pure data — saving and rendering are the caller's business.
/// </summary>
/// <param name="Project">The arrangement, ready to save or render.</param>
/// <param name="TempoBpm">The single tempo every clip is warped to.</param>
/// <param name="Transitions">One entry per join, in play order.</param>
/// <param name="Rejected">Why the set is not longer than it is, one line per reason — mostly candidates that
/// never reached the timeline, but NOT only those. Two <see cref="RejectReason"/> members are explicit
/// non-rejections: <see cref="RejectReason.LengthCapReached"/> names no track at all (the requested length was
/// reached and the rest were never tried), and <see cref="RejectReason.NoMixOutRunway"/> names a record that
/// IS on the timeline, as its closing clip. A caller counting this list is counting explanations, not
/// failures.</param>
public sealed record DjSetPlan(
    StudioProject Project,
    double TempoBpm,
    IReadOnlyList<SetTransition> Transitions,
    IReadOnlyList<RejectedCandidate> Rejected)
{
    /// <summary>Tracks placed on the timeline.</summary>
    public int TrackCount => Project.Clips.Count;

    /// <summary>Length of the finished set.</summary>
    public double TotalSeconds => Project.DurationSeconds;

    /// <summary>The largest stretch applied to any clip, as a percentage — the taste-critical number.</summary>
    public double MaxWarpPercent => Transitions.Count == 0
        ? 0.0
        : Transitions.Max(t => Math.Max(Math.Abs(t.FromWarpPercent), Math.Abs(t.ToWarpPercent)));

    /// <summary>How many joins are genuinely phase-locked (the rest are short, unwarped blends).</summary>
    public int PhaseLockedCount => Transitions.Count(t => t.PhaseLocked);
}
