namespace Liveolator.Core.Dsp;

/// <summary>
/// Turns a track's measured integrated loudness into the playback gain that brings it to a target level.
/// <para>Without this every clip plays at unity, so a set built from commercial masters — which differ by
/// several dB — steps up and down at every join, the equal-power crossfade sums two records of unequal
/// loudness, and the master limiter works hard through the loud tracks and not at all through the quiet
/// ones. Gain staging is what makes a sequence of records read as one mix.</para>
/// </summary>
public static class LoudnessGain
{
    /// <summary>Most a track may be boosted (+6 dB). Beyond this a boost stops rescuing a quiet or
    /// mis-measured file and simply pins the master limiter for the whole clip.</summary>
    public const double MaxGain = 2.0;

    /// <summary>Most a track may be attenuated (−12 dB) — a floor against a wild measurement, not a
    /// musical limit; real masters need nothing like this much cut.</summary>
    public const double MinGain = 0.25;

    /// <summary>
    /// Linear playback gain that moves <paramref name="integratedLufs"/> to <paramref name="targetLufs"/>,
    /// clamped to <see cref="MinGain"/>..<see cref="MaxGain"/>. An unmeasured track stays at unity: leaving
    /// it alone is honest, where guessing a level would silently mis-balance the mix.
    /// </summary>
    public static double For(double? integratedLufs, double targetLufs)
    {
        if (integratedLufs is not double lufs || double.IsNaN(lufs) || double.IsInfinity(lufs))
            return 1.0;

        double gain = Math.Pow(10.0, (targetLufs - lufs) / 20.0);
        return Math.Clamp(gain, MinGain, MaxGain);
    }
}
