namespace Liveolator.Core.Studio;

/// <summary>
/// Pure per-clip level math: the effective linear amplitude of a <see cref="StudioClip"/> at a
/// timeline instant, combining the clip's static <see cref="StudioClip.Gain"/> with a linear fade-in
/// ramp at its head and a linear fade-out ramp at its tail.
/// <para>MVP uses <b>linear</b> (amplitude) ramps, not equal-power — a constant-power crossfade would
/// need a sqrt/cosine law; that is a deliberate follow-up, not this slice.</para>
/// </summary>
public static class ClipGain
{
    /// <summary>
    /// The effective gain of <paramref name="clip"/> at <paramref name="timelineSeconds"/>:
    /// <c>clip.Gain * fadeIn(t) * fadeOut(t)</c>, clamped to <c>[0, +inf)</c>. The fade-in ramps
    /// 0 -&gt; 1 linearly across <see cref="StudioClip.FadeInSeconds"/> from the clip start; the
    /// fade-out ramps 1 -&gt; 0 linearly across <see cref="StudioClip.FadeOutSeconds"/> up to the clip
    /// end. Open-ended clips (no <see cref="StudioClip.TimelineEndSeconds"/>) have no fade-out. Zero
    /// fades collapse to just the static gain. Outside the clip's active window the gain is 0.
    /// </summary>
    public static double EffectiveGainAt(StudioClip clip, double timelineSeconds)
    {
        ArgumentNullException.ThrowIfNull(clip);

        double start = clip.TimelineStartSeconds;
        double? end = clip.TimelineEndSeconds;

        // Outside the clip's sounding window contributes nothing (half-open: [start, end)).
        if (timelineSeconds < start)
            return 0.0;
        if (end is { } e && timelineSeconds >= e)
            return 0.0;

        double gain = Math.Max(0.0, clip.Gain);
        gain *= FadeInFactor(clip, timelineSeconds, start);
        gain *= FadeOutFactor(clip, timelineSeconds, end);
        return Math.Max(0.0, gain);
    }

    // Linear 0 -> 1 over [start, start + FadeInSeconds]; 1.0 once past the ramp (or when no fade-in).
    private static double FadeInFactor(StudioClip clip, double t, double start)
    {
        double fade = clip.FadeInSeconds;
        if (fade <= 0.0)
            return 1.0;

        double elapsed = t - start;
        return elapsed >= fade ? 1.0 : Math.Clamp(elapsed / fade, 0.0, 1.0);
    }

    // Linear 1 -> 0 over [end - FadeOutSeconds, end]; 1.0 before the ramp (or when open-ended / no fade-out).
    private static double FadeOutFactor(StudioClip clip, double t, double? end)
    {
        double fade = clip.FadeOutSeconds;
        if (fade <= 0.0 || end is not { } e)
            return 1.0;

        double remaining = e - t;
        return remaining >= fade ? 1.0 : Math.Clamp(remaining / fade, 0.0, 1.0);
    }
}
