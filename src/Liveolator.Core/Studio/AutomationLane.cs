namespace Liveolator.Core.Studio;

/// <summary>
/// A timeline automation curve for one control on one deck: an ordered list of keyframes the
/// transport/render samples with <see cref="ValueAt"/>. Pure data + interpolation; the mapping to a
/// <c>PerformanceAction</c> lives in the arrangement scheduler (Phase 3).
/// </summary>
/// <remarks>Keyframes are expected in non-decreasing <see cref="AutomationKeyframe.TimeSeconds"/>
/// order; the UI/planner maintains that ordering.</remarks>
public sealed record AutomationLane(
    AutomationTarget Target,
    int DeckSlot,
    IReadOnlyList<AutomationKeyframe> Keyframes)
{
    /// <summary>
    /// The interpolated control value (0..1) at <paramref name="timeSeconds"/>: held flat before the
    /// first keyframe and after the last, linearly interpolated between adjacent keyframes.
    /// </summary>
    /// <exception cref="InvalidOperationException">The lane has no keyframes.</exception>
    public double ValueAt(double timeSeconds)
    {
        if (Keyframes.Count == 0)
            throw new InvalidOperationException("Automation lane has no keyframes to evaluate.");

        AutomationKeyframe first = Keyframes[0];
        if (timeSeconds <= first.TimeSeconds)
            return first.Value;

        AutomationKeyframe last = Keyframes[^1];
        if (timeSeconds >= last.TimeSeconds)
            return last.Value;

        // The last keyframe at or before t — so coincident-time keyframes resolve right-continuously
        // (the later value at a step wins the instant it lands), then interpolate to the next one.
        int idx = 0;
        for (int i = 0; i < Keyframes.Count && Keyframes[i].TimeSeconds <= timeSeconds; i++)
            idx = i;

        AutomationKeyframe a = Keyframes[idx];
        AutomationKeyframe b = Keyframes[idx + 1];
        double span = b.TimeSeconds - a.TimeSeconds;
        if (span <= 0)
            return b.Value;
        double t = (timeSeconds - a.TimeSeconds) / span;
        return a.Value + ((b.Value - a.Value) * t);
    }
}
