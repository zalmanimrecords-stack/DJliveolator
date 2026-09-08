namespace Liveolator.Core.Studio;

/// <summary>
/// Maps between a warped clip's own source seconds and the project timeline when the tempo MOVES.
/// <para>Under one flat tempo the two are related by a single factor, so the arranger divides and is done.
/// A ramped set has no such factor: the source a clip consumes over a span is the integral of the tempo
/// across it, and placing the next clip is that integral inverted. Both live here so the geometry is
/// stated once and can be tested without audio.</para>
/// <para><see cref="TempoCurve"/> is piecewise LINEAR, so each segment's integral is its trapezoid —
/// exact, not a numerical approximation — and inverting one segment is a quadratic. Held flat outside the
/// keyframes, matching <see cref="TempoCurve.TempoAt"/>.</para>
/// </summary>
public static class TempoIntegral
{
    /// <summary>
    /// The source seconds of a track at <paramref name="sourceBpm"/> consumed between
    /// <paramref name="fromSeconds"/> and <paramref name="toSeconds"/> on the timeline. Equal to
    /// <c>(to - from) * tempo / sourceBpm</c> when the tempo is flat.
    /// </summary>
    public static double SourceSeconds(
        TempoCurve curve, double defaultBpm, double sourceBpm, double fromSeconds, double toSeconds)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceBpm);
        if (toSeconds <= fromSeconds)
            return 0.0;

        double beats = 0.0;
        foreach ((double a, double b) in Segments(curve, fromSeconds, toSeconds))
        {
            // Trapezoid: the mean of a linear ramp's endpoints is its mean value over the span.
            double mean = (curve.TempoAt(a, defaultBpm) + curve.TempoAt(b, defaultBpm)) / 2.0;
            beats += mean * (b - a);
        }

        return beats / sourceBpm;
    }

    /// <summary>
    /// The timeline instant at which <paramref name="sourceSeconds"/> of a track at
    /// <paramref name="sourceBpm"/> have been consumed, starting from <paramref name="fromSeconds"/>.
    /// The inverse of <see cref="SourceSeconds"/>.
    /// </summary>
    public static double TimelineSecondsForSource(
        TempoCurve curve, double defaultBpm, double sourceBpm, double fromSeconds, double sourceSeconds)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceBpm);
        if (sourceSeconds <= 0.0)
            return fromSeconds;

        double remaining = sourceSeconds * sourceBpm;   // beats still to consume
        double t = fromSeconds;

        // Walk the keyframe segments the span crosses; the last one is open-ended (the curve is held flat
        // after the final keyframe), so the loop always terminates in the tail solve below.
        foreach ((double a, double b) in Segments(curve, fromSeconds, double.PositiveInfinity))
        {
            double tempoA = curve.TempoAt(a, defaultBpm);
            double tempoB = curve.TempoAt(b, defaultBpm);
            double span = b - a;
            double available = (tempoA + tempoB) / 2.0 * span;
            if (available >= remaining)
                return a + SolveWithin(tempoA, tempoB, span, remaining);

            remaining -= available;
            t = b;
        }

        // Past the last keyframe the tempo is constant, so what is left divides out.
        double tail = curve.TempoAt(t, defaultBpm);
        return tail > 0.0 ? t + (remaining / tail) : t;
    }

    /// <summary>
    /// Where inside one linear segment <paramref name="beats"/> beats have elapsed. With tempo
    /// <c>T(x) = a + (b-a)x/span</c> the beats to x are <c>a*x + (b-a)x^2/(2*span)</c>; solving that
    /// quadratic is what makes the ramp exact rather than stepped.
    /// </summary>
    private static double SolveWithin(double tempoA, double tempoB, double span, double beats)
    {
        if (span <= 0.0)
            return 0.0;

        double slope = (tempoB - tempoA) / span;
        if (Math.Abs(slope) < 1e-12)
            return tempoA > 0.0 ? Math.Min(span, beats / tempoA) : span;

        // (slope/2)x^2 + tempoA*x - beats = 0, taking the root inside the segment.
        double disc = (tempoA * tempoA) + (2.0 * slope * beats);
        if (disc < 0.0)
            return span;
        double x = (-tempoA + Math.Sqrt(disc)) / slope;
        return Math.Clamp(x, 0.0, span);
    }

    /// <summary>
    /// The curve's segments clipped to <c>[from, to]</c>, each one linear end to end so the trapezoid
    /// above is exact. An empty or exhausted curve yields a single open segment — flat tempo.
    /// </summary>
    private static IEnumerable<(double From, double To)> Segments(TempoCurve curve, double from, double to)
    {
        double cursor = from;
        foreach (TempoKeyframe key in curve.Keyframes)
        {
            if (key.TimeSeconds <= cursor)
                continue;
            if (key.TimeSeconds >= to)
                break;
            yield return (cursor, key.TimeSeconds);
            cursor = key.TimeSeconds;
        }

        if (cursor < to)
            yield return (cursor, double.IsPositiveInfinity(to) ? cursor + LongTailSeconds : to);
    }

    /// <summary>How far past the last keyframe one open segment reaches while inverting. Any span longer
    /// than a record is enough; the tail solve below the loop catches whatever is left.</summary>
    private const double LongTailSeconds = 3600.0;
}
