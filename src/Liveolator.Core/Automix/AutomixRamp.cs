namespace Liveolator.Core.Automix;

/// <summary>
/// Shared curve helpers for the auto-mix style profiles: linear and smoothstep segment ramps over a
/// progress interval. Pure math, clamped on both axes so a profile can never emit an out-of-range
/// mixer value.
/// </summary>
internal static class AutomixRamp
{
    /// <summary>
    /// Linear ramp: holds <paramref name="valueStart"/> before <paramref name="progressStart"/>,
    /// <paramref name="valueEnd"/> after <paramref name="progressEnd"/>, interpolating between.
    /// </summary>
    public static double Linear(
        double progress, double progressStart, double progressEnd, double valueStart, double valueEnd)
    {
        double t = Segment(progress, progressStart, progressEnd);
        return valueStart + ((valueEnd - valueStart) * t);
    }

    /// <summary>
    /// Smoothstep ramp (3t²−2t³) over the same segment semantics as <see cref="Linear"/> — used for
    /// the one-beat bass swap so the hand-over has no corner.
    /// </summary>
    public static double Smooth(
        double progress, double progressStart, double progressEnd, double valueStart, double valueEnd)
    {
        double t = Segment(progress, progressStart, progressEnd);
        double s = t * t * (3.0 - (2.0 * t));
        return valueStart + ((valueEnd - valueStart) * s);
    }

    private static double Segment(double progress, double start, double end)
    {
        if (end <= start)
            return progress >= end ? 1.0 : 0.0;
        return Math.Clamp((progress - start) / (end - start), 0.0, 1.0);
    }
}
