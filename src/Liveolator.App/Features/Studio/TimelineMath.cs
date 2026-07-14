namespace Liveolator.App.Features.Studio;

/// <summary>
/// Pure timeline geometry for the STUDIO arrangement view: converting between horizontal pixels and
/// seconds at a given zoom, and snapping a time to a grid. No Avalonia types, so it unit-tests without
/// a render — the interactive handlers in the view delegate their math here.
/// </summary>
public static class TimelineMath
{
    /// <summary>Seconds at a horizontal pixel offset, clamped to ≥ 0. A non-positive zoom yields 0.</summary>
    public static double SecondsFromX(double x, double pixelsPerSecond)
        => pixelsPerSecond <= 0 ? 0 : Math.Max(0, x / pixelsPerSecond);

    /// <summary>The horizontal pixel offset for a timeline position.</summary>
    public static double XFromSeconds(double seconds, double pixelsPerSecond)
        => Math.Max(0, seconds) * pixelsPerSecond;

    /// <summary>
    /// Snap <paramref name="seconds"/> to the nearest multiple of <paramref name="gridSeconds"/>
    /// (e.g. a beat). A non-positive grid returns the value unsnapped. Never returns a negative time.
    /// </summary>
    public static double Snap(double seconds, double gridSeconds)
    {
        if (gridSeconds <= 0)
            return Math.Max(0, seconds);
        return Math.Max(0, Math.Round(seconds / gridSeconds) * gridSeconds);
    }

    /// <summary>Seconds-per-beat for a tempo (one beat at <paramref name="bpm"/>); 0 when bpm ≤ 0.</summary>
    public static double BeatSeconds(double bpm) => bpm <= 0 ? 0 : 60.0 / bpm;
}
