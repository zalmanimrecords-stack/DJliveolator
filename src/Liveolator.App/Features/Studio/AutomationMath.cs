using System.Collections.Generic;

namespace Liveolator.App.Features.Studio;

/// <summary>
/// Pure geometry for the automation curve editor: mapping a control value (0..1) to/from a vertical
/// pixel (top = 1, bottom = 0), and hit-testing the nearest keyframe to a pointer. Time↔pixel uses
/// <see cref="TimelineMath"/>. No Avalonia types, so it unit-tests without a render.
/// </summary>
public static class AutomationMath
{
    /// <summary>Control value (0..1) at a vertical pixel; top is 1.0, bottom is 0.0.</summary>
    public static double ValueFromY(double y, double height)
    {
        if (height <= 0)
            return 0;
        return Math.Clamp(1.0 - (y / height), 0, 1);
    }

    /// <summary>The vertical pixel for a control value (0..1); 1.0 sits at the top.</summary>
    public static double YFromValue(double value, double height)
        => (1.0 - Math.Clamp(value, 0, 1)) * Math.Max(0, height);

    /// <summary>
    /// Index of the keyframe nearest the pointer within <paramref name="tolerancePx"/>, or -1 when
    /// none is close enough. Points are (timeSeconds, value); the editor's local x = time × zoom.
    /// </summary>
    public static int NearestPointIndex(
        IReadOnlyList<(double Time, double Value)> points,
        double x, double y, double pixelsPerSecond, double height, double tolerancePx)
    {
        int best = -1;
        double bestDistSq = tolerancePx * tolerancePx;
        for (int i = 0; i < points.Count; i++)
        {
            double px = TimelineMath.XFromSeconds(points[i].Time, pixelsPerSecond);
            double py = YFromValue(points[i].Value, height);
            double dx = px - x;
            double dy = py - y;
            double distSq = (dx * dx) + (dy * dy);
            if (distSq <= bestDistSq)
            {
                bestDistSq = distSq;
                best = i;
            }
        }

        return best;
    }
}
