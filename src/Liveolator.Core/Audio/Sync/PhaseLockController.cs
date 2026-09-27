namespace Liveolator.Core.Audio.Sync;

/// <summary>
/// The continuous phase-lock control law behind professional Sync (doc 11) — the piece that keeps a
/// synced (slave) deck beat-locked to the master <em>over time</em>, the way a pro DJ rig holds a mix.
/// Tempo match (<see cref="TempoSyncCalculator"/>) makes the two decks the same speed and
/// the one-shot phase snap (<see cref="PhaseAlignmentCalculator.PhaseNudgeSeconds"/>) lands them in
/// phase, but sample-rate resampling and floating-point arithmetic let them drift apart over minutes.
/// This controller closes the loop: each tick it measures the residual beat-phase error and applies a
/// tiny, clamped playback-rate correction so the slave eases back onto the grid — no seeking, no
/// clicks, no audible jumps.
/// </summary>
/// <remarks>
/// Pure and stateless — a proportional law, memoryless in the phase error — so it unit-tests under
/// xUnit with no hardware and cannot itself accumulate drift. The engine owns rate application and the
/// host-time cadence; this only answers "given where the two decks are right now, what rate should the
/// slave run, and is it locked?". It is time-stretch independent: it speaks only in rate multipliers and
/// beats, so a future key-lock stretch algorithm changes nothing here (spec: "SyncEngine manages
/// timing"). Off is an engine/UI concern (the controller is only called for an engaged slave).
/// </remarks>
public static class PhaseLockController
{
    /// <summary>
    /// Decide the slave deck's effective playback rate and lock state for one correction tick.
    /// </summary>
    /// <param name="slave">The synced deck's live phase (position / first-beat anchor / effective BPM).</param>
    /// <param name="master">The sync master's live phase — the grid the slave locks onto.</param>
    /// <param name="beatmatchedRate">
    /// The slave's base (tempo-matched) rate from <see cref="TempoSyncCalculator.RateFor"/> — the rate
    /// that makes the two decks the same tempo, before any phase correction.
    /// </param>
    /// <param name="settings">Loop gains and thresholds.</param>
    /// <param name="previousState">
    /// The deck's lock state from the previous tick. Drives the lock-zone hysteresis: an already-Locked
    /// deck holds Locked out to the wider exit tolerance, while a not-yet-locked deck must reach the tight
    /// enter tolerance — so a deck resting on the boundary cannot flip Locked↔Active every tick. Defaults
    /// to <see cref="SyncLockState.Off"/> (use the tight enter tolerance), the safe first-tick behaviour.
    /// </param>
    public static PhaseLockCorrection Correct(
        DeckPhase slave, DeckPhase master, double beatmatchedRate, PhaseLockSettings settings,
        SyncLockState previousState = SyncLockState.Off)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // PHASE SYNCHRONIZATION — the signed beat-phase error, master minus slave, wrapped to
        // (-0.5, 0.5] beats. Positive => the slave is behind the master and must catch up (run faster);
        // negative => ahead (run slower). Wrapping picks the shortest direction so the loop never chases
        // a whole beat around the grid. Position inputs are expected latency-compensated by the caller.
        double errorBeats = PhaseAlignmentCalculator.BeatPhaseError(slave, master);
        double absError = Math.Abs(errorBeats);

        // CONTINUOUS CORRECTION — a proportional law on EVERY tick, lock zone included: nudge the rate by
        // error·gain, hard-clamped to ±MaxCorrection. (base + correction) eases the slave toward zero error;
        // because the error is re-measured from the real playhead every tick, there is no integral term to
        // wind up and no drift to accumulate (DRIFT PREVENTION). Holding the bare beatmatched rate inside the
        // lock zone instead would let a "locked" pair rest anywhere in it, a split the DJ PRO kick comb makes
        // visible. The law is continuous in the error, so there is no rate step at the zone edge to chatter.
        double correction = Math.Clamp(errorBeats * settings.Gain, -settings.MaxCorrection, settings.MaxCorrection);
        double effectiveRate = beatmatchedRate + correction;

        // PHASE LOCK TOLERANCE (with HYSTERESIS) — decides only the REPORTED state. Entering needs the tight
        // LockToleranceBeats; once Locked the deck holds the label out to the wider ExitLockToleranceBeats so
        // measurement jitter on the boundary cannot flicker the Locked badge / LED. The exit tolerance is
        // clamped to at least the enter tolerance so a misconfiguration can never invert the band.
        double lockTolerance = previousState == SyncLockState.Locked
            ? Math.Max(settings.ExitLockToleranceBeats, settings.LockToleranceBeats)
            : settings.LockToleranceBeats;
        if (absError < lockTolerance)
            return new PhaseLockCorrection(
                effectiveRate, SyncLockState.Locked, errorBeats, RequiresReSnap: false, ReSnapSeconds: 0.0);

        // LARGE ERROR HANDLING — if the phase has slipped past the re-snap threshold (the user nudged the
        // platter, a loop dropped the playhead, a track was swapped mid-flight), riding it back at
        // ±MaxCorrection would take too long and sound like a sustained pitch bend. Flag a one-shot
        // beat-snap so the engine seeks the playhead onto the nearest aligned beat. The micro-correction
        // still applies this tick so there is no gap before the seek lands. Reported as Drifting.
        if (absError > settings.ReSnapThresholdBeats)
        {
            double reSnapSeconds = PhaseAlignmentCalculator.PhaseNudgeSeconds(slave, master);
            return new PhaseLockCorrection(
                effectiveRate, SyncLockState.Drifting, errorBeats, RequiresReSnap: true, reSnapSeconds);
        }

        // Between the lock zone and the re-snap threshold the loop is actively pulling the slave in.
        return new PhaseLockCorrection(
            effectiveRate, SyncLockState.Active, errorBeats, RequiresReSnap: false, ReSnapSeconds: 0.0);
    }
}
