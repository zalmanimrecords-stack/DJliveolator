namespace Liveolator.Core.Settings;

/// <summary>
/// Jog-wheel feel. While the deck is PAUSED the wheel scrubs the track like a record under the hand
/// (<see cref="PausedSecondsPerRevolution"/>). While it PLAYS the wheel is a temporary pitch-bend for
/// beat-matching — it never seeks — shaped by the bend parameters: a velocity deadzone, a linear gain,
/// a hard cap at the pitch rail, EMA smoothing of the tick rate, and a release timeout for endless
/// encoders that send no "release" (the bend snaps back once ticks stop for this long).
/// </summary>
public sealed record JogWheelSettings(
    double PausedSecondsPerRevolution = 1.8,
    double BendMaxFraction = 0.08,
    double BendGainPerRevPerSecond = 0.04,
    double DeadzoneRevPerSecond = 0.05,
    double VelocityEmaAlpha = 0.40,
    double ReleaseTimeoutMs = 120.0)
{
    /// <summary>A full paused turn scans ~1.8 s of track — coarse enough to move, fine enough to cue.</summary>
    public const double DefaultPausedSecondsPerRevolution = 1.8;

    /// <summary>Bend ceiling = the ±8 % pitch rail, so jog-bend and the pitch fader share one range.</summary>
    public const double DefaultBendMaxFraction = 0.08;

    /// <summary>Bend fraction per rev/s; saturates the rail at ~2 rev/s — a comfortable firm nudge.</summary>
    public const double DefaultBendGainPerRevPerSecond = 0.04;

    /// <summary>Below this angular velocity the wheel makes no bend, so a resting hand never detunes.</summary>
    public const double DefaultDeadzoneRevPerSecond = 0.05;

    /// <summary>EMA weight on the newest tick rate (0..1); smooths encoder/pointer jitter.</summary>
    public const double DefaultVelocityEmaAlpha = 0.40;

    /// <summary>An endless encoder sends no release; the bend snaps back once ticks stop for this long.</summary>
    public const double DefaultReleaseTimeoutMs = 120.0;

    public static JogWheelSettings Default { get; } = new();

    public JogWheelSettings Normalized()
        => this with
        {
            PausedSecondsPerRevolution = Positive(PausedSecondsPerRevolution, DefaultPausedSecondsPerRevolution),
            BendMaxFraction = Fraction(BendMaxFraction, DefaultBendMaxFraction),
            BendGainPerRevPerSecond = Positive(BendGainPerRevPerSecond, DefaultBendGainPerRevPerSecond),
            DeadzoneRevPerSecond = NonNegative(DeadzoneRevPerSecond, DefaultDeadzoneRevPerSecond),
            VelocityEmaAlpha = Fraction(VelocityEmaAlpha, DefaultVelocityEmaAlpha),
            ReleaseTimeoutMs = Positive(ReleaseTimeoutMs, DefaultReleaseTimeoutMs),
        };

    private static double Positive(double value, double fallback)
        => double.IsFinite(value) && value > 0.0 ? value : fallback;

    private static double NonNegative(double value, double fallback)
        => double.IsFinite(value) && value >= 0.0 ? value : fallback;

    // A fraction/weight must land in (0, 1]; anything else (0, >1, NaN) falls back to the default.
    private static double Fraction(double value, double fallback)
        => double.IsFinite(value) && value > 0.0 && value <= 1.0 ? value : fallback;
}
