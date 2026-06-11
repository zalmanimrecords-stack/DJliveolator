namespace Liveolator.Core.Automix;

/// <summary>
/// The resolved plan for one auto-mix transition, produced by <see cref="AutomixPreflight"/> before
/// anything is dispatched: the deck roles, the (possibly auto-shortened) length, the effective style
/// after grid-availability degradation, and the incoming deck's mix-in point — or a typed refusal.
/// </summary>
/// <param name="Refusal">Why the transition may not start; <see cref="AutomixRefusal.None"/> = go.</param>
/// <param name="FromSlot">The outgoing (currently audible-dominant) deck slot.</param>
/// <param name="ToSlot">The incoming deck slot.</param>
/// <param name="PlannedBars">Transition length in bars after fitting to the outgoing track's remaining time.</param>
/// <param name="EffectiveStyle">The requested style, degraded to CrossFade when a beat grid is missing.</param>
/// <param name="MixInSeconds">Where the incoming deck starts (its first-beat anchor, or 0).</param>
public sealed record AutomixPlan(
    AutomixRefusal Refusal,
    int FromSlot,
    int ToSlot,
    int PlannedBars,
    AutomixStyle EffectiveStyle,
    double MixInSeconds)
{
    /// <summary>True when the transition may start.</summary>
    public bool IsAllowed => Refusal == AutomixRefusal.None;

    /// <summary>A refused plan carrying only the reason.</summary>
    public static AutomixPlan Refused(AutomixRefusal reason)
        => new(reason, FromSlot: -1, ToSlot: -1, PlannedBars: 0, AutomixStyle.CrossFade, MixInSeconds: 0.0);
}
