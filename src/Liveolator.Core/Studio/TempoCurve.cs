namespace Liveolator.Core.Studio;

/// <summary>
/// The project-wide tempo automation: a time-ordered set of <see cref="TempoKeyframe"/>s giving the
/// arrangement tempo (BPM) over time. Warped clips follow this curve. Pure data + interpolation
/// (mirrors <see cref="AutomationLane.ValueAt"/>, but in BPM): held flat before the first keyframe and
/// after the last, linearly interpolated between, right-continuous at coincident times. An empty curve
/// means "use the project's fixed tempo" — callers pass that as the default.
/// </summary>
public sealed record TempoCurve(IReadOnlyList<TempoKeyframe> Keyframes)
{
    /// <summary>A curve with no keyframes — <see cref="TempoAt"/> always returns the supplied default.</summary>
    public static TempoCurve Empty { get; } = new(Array.Empty<TempoKeyframe>());

    /// <summary>
    /// The tempo (BPM) at <paramref name="timeSeconds"/>: <paramref name="defaultBpm"/> when the curve is
    /// empty, otherwise the interpolated keyframe value (held flat outside the keyframe range).
    /// </summary>
    public double TempoAt(double timeSeconds, double defaultBpm)
    {
        if (Keyframes.Count == 0)
            return defaultBpm;

        TempoKeyframe first = Keyframes[0];
        if (timeSeconds <= first.TimeSeconds)
            return first.Bpm;

        TempoKeyframe last = Keyframes[^1];
        if (timeSeconds >= last.TimeSeconds)
            return last.Bpm;

        // Last keyframe at or before t (right-continuous at coincident times), then interpolate to the next.
        int idx = 0;
        for (int i = 0; i < Keyframes.Count && Keyframes[i].TimeSeconds <= timeSeconds; i++)
            idx = i;

        TempoKeyframe a = Keyframes[idx];
        TempoKeyframe b = Keyframes[idx + 1];
        double span = b.TimeSeconds - a.TimeSeconds;
        if (span <= 0)
            return b.Bpm;
        double f = (timeSeconds - a.TimeSeconds) / span;
        return a.Bpm + ((b.Bpm - a.Bpm) * f);
    }
}
