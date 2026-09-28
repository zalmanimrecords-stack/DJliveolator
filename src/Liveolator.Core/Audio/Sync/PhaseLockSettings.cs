namespace Liveolator.Core.Audio.Sync;

/// <summary>
/// Tunables for the <see cref="PhaseLockController"/> loop. Defaults follow professional-DJ practice: a
/// proportional gain that settles a phase error in seconds with a steady-state correction far below 1%, a
/// catch-up ceiling, a tight lock zone, and a re-snap threshold past which the deck is jumped rather than
/// ridden back on pitch.
/// </summary>
/// <param name="Gain">Proportional gain: rate correction per beat of phase error (before clamping). The error
/// decays with τ = 60 / (bpm · Gain): 0.08 gives ~6 s at 125 BPM. A settled pair's
/// residual of a few ms asks for a few hundredths of a percent of rate. The largest ride-in correction,
/// 0.08 × the 0.25-beat re-snap threshold = 2%, moves tempo but not pitch under the key-lock that Sync
/// engages.</param>
/// <param name="MaxCorrection">Hard ceiling on the rate correction, ± this value. A catch-up ceiling, not a
/// steady-state value: at the default gain it binds only past the re-snap threshold, i.e. on the one tick
/// before the seek lands, so it caps that blip and any retuned gain.</param>
/// <param name="LockToleranceBeats">ENTER-lock tolerance: a not-yet-locked deck must fall below this absolute phase error (beats) to be REPORTED Locked (0.01 ≈ 4.8 ms at 125 BPM).</param>
/// <param name="ExitLockToleranceBeats">EXIT-lock tolerance: an already-Locked deck keeps the Locked label until the error widens past this (≥ <see cref="LockToleranceBeats"/>). The band between the two stops the reported state flickering on the boundary; it does not pause the correction.</param>
/// <param name="ReSnapThresholdBeats">Above this absolute phase error (beats) the engine seeks a one-shot beat-snap instead of riding pitch.</param>
/// <param name="OutputLatencySeconds">Total output latency (buffer + device) subtracted from measured positions so corrections reference what the listener actually hears.</param>
public sealed record PhaseLockSettings(
    double Gain = 0.08,
    double MaxCorrection = 0.03,
    double LockToleranceBeats = 0.01,
    double ExitLockToleranceBeats = 0.02,
    double ReSnapThresholdBeats = 0.25,
    double OutputLatencySeconds = 0.0)
{
    /// <summary>The professional-default settings.</summary>
    public static PhaseLockSettings Default { get; } = new();
}
