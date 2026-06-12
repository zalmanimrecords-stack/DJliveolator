using Liveolator.Core.Mixer;

namespace Liveolator.Core.Studio;

/// <summary>
/// A planned blend from the previous set entry into this one: what kind of handover, how long it
/// lasts (in beats, so it survives tempo edits), how the crossfader tapers, and where it sits in
/// time. Pure data — the live automix engine (Phase 5) and the offline renderer (Phase 6) read it;
/// neither is referenced here, preserving the Core purity boundary.
/// </summary>
public sealed record StudioTransition(
    TransitionKind Kind,
    double LengthBeats,
    CrossfaderCurve Curve,
    TransitionAnchor Anchor)
{
    /// <summary>An instant cut — the safe default when two tracks cannot be beat-matched.</summary>
    public static StudioTransition Cut { get; } =
        new(TransitionKind.Cut, 0, CrossfaderCurve.Sharp, TransitionAnchor.TailOverlap);
}
