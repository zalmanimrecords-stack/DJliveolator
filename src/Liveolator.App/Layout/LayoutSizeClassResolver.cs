using System;

namespace Liveolator.App.Layout;

/// <summary>
/// Maps a host width (logical px) to a <see cref="LayoutSizeClass"/> with hysteresis: a class change
/// only commits once the width crosses the relevant boundary by <see cref="Hysteresis"/>. This dead-band
/// stops a window dragged near a breakpoint from thrashing between layouts (paired in the views with the
/// "defer discrete reflow until no deck is playing" rule, so the live surface never jumps under the DJ's hands).
/// Pure logic — no Avalonia types — so it unit-tests without a UI thread.
/// </summary>
public static class LayoutSizeClassResolver
{
    /// <summary>Upper edge of <see cref="LayoutSizeClass.Compact"/> (covers 1366x768 with chrome).</summary>
    public const double CompactMax = 1180;

    /// <summary>Upper edge of <see cref="LayoutSizeClass.Standard"/> (the design baseline).</summary>
    public const double StandardMax = 1700;

    /// <summary>Upper edge of <see cref="LayoutSizeClass.Wide"/>; above this is <see cref="LayoutSizeClass.Ultra"/>.</summary>
    public const double WideMax = 2400;

    /// <summary>Dead-band (logical px) applied on both sides of every boundary.</summary>
    public const double Hysteresis = 50;

    /// <summary>
    /// The tier <paramref name="width"/> falls into ignoring the current state (no hysteresis).
    /// </summary>
    public static LayoutSizeClass Classify(double width)
    {
        if (width < CompactMax) return LayoutSizeClass.Compact;
        if (width < StandardMax) return LayoutSizeClass.Standard;
        if (width < WideMax) return LayoutSizeClass.Wide;
        return LayoutSizeClass.Ultra;
    }

    /// <summary>
    /// Resolves the tier for <paramref name="width"/> given the <paramref name="current"/> tier,
    /// only leaving the current tier once the width has moved past its boundary by <see cref="Hysteresis"/>.
    /// Handles multi-tier jumps (e.g. maximize) by snapping straight to the natural tier once a boundary is crossed.
    /// </summary>
    public static LayoutSizeClass Resolve(double width, LayoutSizeClass current)
    {
        if (double.IsNaN(width) || width <= 0)
            return current;

        // Grow: only step up once past this tier's upper edge by the dead-band.
        if (width >= UpperEdge(current) + Hysteresis)
            return Classify(width);

        // Shrink: only step down once below this tier's lower edge by the dead-band.
        if (width <= LowerEdge(current) - Hysteresis)
            return Classify(width);

        return current;
    }

    private static double UpperEdge(LayoutSizeClass cls) => cls switch
    {
        LayoutSizeClass.Compact => CompactMax,
        LayoutSizeClass.Standard => StandardMax,
        LayoutSizeClass.Wide => WideMax,
        _ => double.PositiveInfinity,
    };

    private static double LowerEdge(LayoutSizeClass cls) => cls switch
    {
        LayoutSizeClass.Ultra => WideMax,
        LayoutSizeClass.Wide => StandardMax,
        LayoutSizeClass.Standard => CompactMax,
        _ => double.NegativeInfinity,
    };
}
