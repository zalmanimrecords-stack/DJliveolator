namespace Liveolator.Core.Audio;

/// <summary>
/// Pure jog-wheel math shared by every jog surface (on-screen platter, hardware encoder). It maps the
/// wheel's angular velocity — signed revolutions/second, clockwise positive — onto a temporary
/// pitch-bend fraction for beat-matching: a deadzone so a resting hand never detunes the deck, a linear
/// gain, and a hard cap at the pitch rail. No state, no clock — trivially unit-tested.
/// </summary>
public static class JogMath
{
    /// <summary>
    /// The temporary pitch-bend fraction for a wheel turning at <paramref name="revPerSecond"/>
    /// (clockwise/forward positive → speed up). Below <paramref name="deadzoneRevPerSecond"/> the result
    /// is exactly 0; otherwise it is <paramref name="gainPerRevPerSecond"/> × velocity, clamped to
    /// ±<paramref name="maxFraction"/>. Non-finite input yields 0.
    /// </summary>
    public static double BendFraction(
        double revPerSecond,
        double gainPerRevPerSecond,
        double deadzoneRevPerSecond,
        double maxFraction)
    {
        if (!double.IsFinite(revPerSecond))
            return 0.0;
        if (Math.Abs(revPerSecond) < deadzoneRevPerSecond)
            return 0.0;
        return Math.Clamp(revPerSecond * gainPerRevPerSecond, -maxFraction, maxFraction);
    }
}
