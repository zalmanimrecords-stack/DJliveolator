namespace Liveolator.Core.Audio.Sync;

/// <summary>
/// Tunables for the <see cref="PhaseLockController"/> loop. Defaults follow professional-DJ practice:
/// a gentle proportional gain with a sub-percent correction ceiling (inaudible pitch movement), a tight
/// lock zone, and a re-snap threshold past which the deck is jumped rather than ridden back on pitch.
/// </summary>
/// <param name="Gain">Proportional gain: rate correction per beat of phase error (before clamping).</param>
/// <param name="MaxCorrection">Hard ceiling on the rate correction, ± this value — keeps the pitch shift inaudible.</param>
/// <param name="LockToleranceBeats">ENTER-lock tolerance: a not-yet-locked deck must fall below this absolute phase error (beats) to be considered Locked.</param>
/// <param name="ExitLockToleranceBeats">EXIT-lock tolerance: an already-Locked deck holds Locked until the error widens past this (≥ <see cref="LockToleranceBeats"/>). The dead-band between the two stops Locked↔Active chatter on the boundary.</param>
/// <param name="ReSnapThresholdBeats">Above this absolute phase error (beats) the engine seeks a one-shot beat-snap instead of riding pitch.</param>
/// <param name="OutputLatencySeconds">Total output latency (buffer + device) subtracted from measured positions so corrections reference what the listener actually hears.</param>
public sealed record PhaseLockSettings(
    double Gain = 0.01,
    double MaxCorrection = 0.03,
    double LockToleranceBeats = 0.02,
    double ExitLockToleranceBeats = 0.04,
    double ReSnapThresholdBeats = 0.25,
    double OutputLatencySeconds = 0.0)
{
    /// <summary>The professional-default settings.</summary>
    public static PhaseLockSettings Default { get; } = new();
}
