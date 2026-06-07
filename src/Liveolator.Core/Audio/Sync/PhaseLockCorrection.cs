namespace Liveolator.Core.Audio.Sync;

/// <summary>
/// One tick's output from <see cref="PhaseLockController"/>: the rate the slave deck should run, the
/// resulting lock state, the measured phase error (for diagnostics / the UI lock indicator), and — when
/// the deck has slipped too far to ride back on pitch alone — a one-shot beat-snap the engine should
/// seek by.
/// </summary>
/// <param name="EffectiveRate">Playback-rate multiplier to apply to the slave (beatmatched rate + clamped correction).</param>
/// <param name="State">The slave's sync-lock state this tick.</param>
/// <param name="ErrorBeats">Signed beat-phase error (master − slave), wrapped to (-0.5, 0.5].</param>
/// <param name="RequiresReSnap">True when the engine should also seek the playhead by <see cref="ReSnapSeconds"/>.</param>
/// <param name="ReSnapSeconds">Signed seconds to nudge the slave playhead onto the nearest aligned beat (0 unless <see cref="RequiresReSnap"/>).</param>
public readonly record struct PhaseLockCorrection(
    double EffectiveRate,
    SyncLockState State,
    double ErrorBeats,
    bool RequiresReSnap,
    double ReSnapSeconds);
