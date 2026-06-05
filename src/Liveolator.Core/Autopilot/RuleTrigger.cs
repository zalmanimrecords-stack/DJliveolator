namespace Liveolator.Core.Autopilot;

/// <summary>When a rule fires (doc 10).</summary>
/// <param name="Kind">The trigger kind.</param>
/// <param name="N">Count for EveryNBeats/EveryNBars, or the percent (0..100) for OnTrackPosition;
/// ignored for OnDownbeat.</param>
public sealed record RuleTrigger(TriggerKind Kind, int N = 0);
