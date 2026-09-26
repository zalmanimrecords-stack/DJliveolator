namespace Liveolator.Core.Mixer;

/// <summary>
/// One AUTO crossfade: the crossfader travels in a straight line from where it was to the far side, and the
/// move always takes the whole MIX SEC however far it had to go (owner decision: "10 seconds" means ten
/// seconds). Linear in position, so the selected <see cref="CrossfaderCurve"/> shapes the gains exactly as a
/// hand move would. Also owns the MIX SEC range and its knob mapping.
/// </summary>
public sealed record AutoCrossfadeRamp(double From, double To, double DurationSeconds, double StartSeconds)
{
    /// <summary>Longest MIX SEC offered.</summary>
    public const double MaxSeconds = 20.0;

    /// <summary>MIX SEC until the DJ turns the knob.</summary>
    public const double DefaultSeconds = 10.0;

    /// <summary>A fade toward the side the crossfader is not on; the centre counts as the A side.</summary>
    public static AutoCrossfadeRamp TowardFarSide(double position, double durationSeconds, double nowSeconds)
        => new(position, position <= 0.5 ? 1.0 : 0.0, durationSeconds, nowSeconds);

    /// <summary>The crossfader position at <paramref name="nowSeconds"/>; <see cref="To"/> once complete.</summary>
    public double PositionAt(double nowSeconds)
        => IsCompleteAt(nowSeconds)
            ? To
            : From + (To - From) * Math.Max(0.0, (nowSeconds - StartSeconds) / DurationSeconds);

    /// <summary>True once the whole duration has elapsed (immediately for a 0-second fade).</summary>
    public bool IsCompleteAt(double nowSeconds) => nowSeconds - StartSeconds >= DurationSeconds;

    /// <summary>Whole seconds in 0..<see cref="MaxSeconds"/>; NaN falls back to the default.</summary>
    public static double ClampSeconds(double seconds)
        => double.IsNaN(seconds) ? DefaultSeconds : Math.Round(Math.Clamp(seconds, 0.0, MaxSeconds));

    /// <summary>MIX SEC knob position (0..1) → seconds.</summary>
    public static double KnobToSeconds(double knob) => ClampSeconds(knob * MaxSeconds);

    /// <summary>Seconds → MIX SEC knob position (0..1).</summary>
    public static double SecondsToKnob(double seconds) => ClampSeconds(seconds) / MaxSeconds;
}
