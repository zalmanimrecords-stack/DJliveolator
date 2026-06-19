namespace Liveolator.Core.Beat;

/// <summary>The grid nudge a single onset observation calls for.</summary>
/// <param name="BeatAdjustment">
/// Signed beats to remove from the grid's beat anchor: positive pulls the grid forward in time (toward
/// an onset that landed after the beat), negative pulls it back. Already clamped to the max step.
/// </param>
/// <param name="Applied">False when the error was inside the deadband and nothing should change.</param>
public readonly record struct PhaseCorrection(double BeatAdjustment, bool Applied);

/// <summary>
/// A software phase-locked loop for the beat grid: given where an onset landed relative to the beat, it
/// returns a small, clamped correction that pulls the grid's phase onto the onset. The tempo estimator
/// fixes the beat <em>period</em>; this keeps the beat <em>phase</em> aligned to the audio over time, so
/// the shared clock — and the visuals riding it — don't drift off the kick during a long track (doc 03).
/// Pure and hardware-free; the realtime clock measures the phase and applies the correction.
/// </summary>
/// <remarks>
/// Proportional control on purpose: a fraction of the error each onset (the gain), hard-clamped per step
/// so a stray onset can never jump the beat, with a deadband so an already-aligned grid sits still
/// instead of jittering. This is drift correction, not re-detection — the period is owned by the tempo
/// stage.
/// </remarks>
public sealed class OnsetPhaseLock
{
    private readonly double _gain;
    private readonly double _maxBeatStep;
    private readonly double _deadband;

    /// <param name="gain">Fraction of the phase error corrected per onset (0..1). Higher = faster, jumpier.</param>
    /// <param name="maxBeatStep">Hard cap on the per-onset correction, in beats.</param>
    /// <param name="deadband">Phase errors smaller than this (in beats) are left alone.</param>
    public OnsetPhaseLock(double gain = 0.1, double maxBeatStep = 0.05, double deadband = 0.005)
    {
        if (gain <= 0.0 || gain > 1.0)
            throw new ArgumentOutOfRangeException(nameof(gain), gain, "Gain must be in (0, 1].");
        if (maxBeatStep <= 0.0 || maxBeatStep > 0.5)
            throw new ArgumentOutOfRangeException(nameof(maxBeatStep), maxBeatStep, "Max step must be in (0, 0.5].");
        if (deadband < 0.0 || deadband >= 0.5)
            throw new ArgumentOutOfRangeException(nameof(deadband), deadband, "Deadband must be in [0, 0.5).");

        _gain = gain;
        _maxBeatStep = maxBeatStep;
        _deadband = deadband;
    }

    /// <summary>
    /// Correction for an onset observed at <paramref name="beatPhaseAtOnset"/> (0..1, where 0 is exactly
    /// on the beat). The error is taken to the nearest beat, so an onset at phase 0.9 reads as 0.1 beats
    /// early (a negative correction), never as 0.9 beats late.
    /// </summary>
    public PhaseCorrection Correct(double beatPhaseAtOnset)
    {
        double phase = beatPhaseAtOnset - Math.Floor(beatPhaseAtOnset); // wrap into [0, 1)
        double error = phase >= 0.5 ? phase - 1.0 : phase;             // signed, [-0.5, 0.5)

        if (Math.Abs(error) < _deadband)
            return new PhaseCorrection(0.0, false);

        double adjustment = Math.Clamp(_gain * error, -_maxBeatStep, _maxBeatStep);
        return new PhaseCorrection(adjustment, true);
    }
}
