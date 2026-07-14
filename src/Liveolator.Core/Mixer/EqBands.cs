namespace Liveolator.Core.Mixer;

/// <summary>
/// Per-deck 3-band EQ settings (doc 11), each band a normalized 0..1 control where
/// <see cref="Unity"/> (0.5) is flat/no change, 0 is full cut and 1 is full boost. Kept as plain
/// normalized values so the model serializes cleanly (doc 13) and matches the 0..1 fader/knob
/// convention of <see cref="Actions.ActionInputMode.Absolute"/>; turning these into filter
/// coefficients is <see cref="MixerMath"/>'s job (strict layer separation).
/// </summary>
/// <param name="Low">Low-band control, 0..1 (0.5 = flat).</param>
/// <param name="Mid">Mid-band control, 0..1 (0.5 = flat).</param>
/// <param name="High">High-band control, 0..1 (0.5 = flat).</param>
public sealed record EqBands(double Low, double Mid, double High)
{
    /// <summary>The neutral position of a single band (no boost, no cut).</summary>
    public const double Unity = 0.5;

    /// <summary>All three bands flat — the default for a freshly loaded deck.</summary>
    public static EqBands Flat { get; } = new(Unity, Unity, Unity);

    /// <summary>Returns a copy with one band replaced, clamped to 0..1.</summary>
    public EqBands With(EqBand band, double value)
    {
        double v = Math.Clamp(value, 0.0, 1.0);
        return band switch
        {
            EqBand.Low => this with { Low = v },
            EqBand.Mid => this with { Mid = v },
            EqBand.High => this with { High = v },
            _ => throw new ArgumentOutOfRangeException(nameof(band), band, "Unknown EQ band."),
        };
    }
}

/// <summary>Identifies which of the three EQ bands an action targets.</summary>
public enum EqBand
{
    Low = 0,
    Mid = 1,
    High = 2,
}
