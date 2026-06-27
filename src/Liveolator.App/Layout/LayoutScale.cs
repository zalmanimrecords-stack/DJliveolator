namespace Liveolator.App.Layout;

/// <summary>
/// Quantized scale multipliers per <see cref="LayoutSizeClass"/>. Control/font sizes are multiplied by
/// these discrete steps (not driven by a continuous function of width) so computed sizes are stable and
/// hint cleanly across resizes. Compact and Standard stay at 1.0 — the laptop tier reflows rather than
/// shrinks, so its controls never drop below the live-performance hit-target floor.
/// </summary>
public static class LayoutScale
{
    public static double For(LayoutSizeClass cls) => cls switch
    {
        LayoutSizeClass.Compact => 1.0,
        LayoutSizeClass.Standard => 1.0,
        LayoutSizeClass.Wide => 1.2,
        LayoutSizeClass.Ultra => 1.4,
        _ => 1.0,
    };

    /// <summary>The lowercase style-class name carried on the shell Window for each tier.</summary>
    public static string StyleClass(LayoutSizeClass cls) => cls switch
    {
        LayoutSizeClass.Compact => "compact",
        LayoutSizeClass.Standard => "standard",
        LayoutSizeClass.Wide => "wide",
        LayoutSizeClass.Ultra => "ultra",
        _ => "standard",
    };

    /// <summary>The tier for a style-class name (inverse of <see cref="StyleClass"/>); unknown/absent =
    /// Standard. Lets a view read the active tier off the shell Window's classes.</summary>
    public static LayoutSizeClass FromStyleClass(string? styleClass) => styleClass switch
    {
        "compact" => LayoutSizeClass.Compact,
        "wide" => LayoutSizeClass.Wide,
        "ultra" => LayoutSizeClass.Ultra,
        _ => LayoutSizeClass.Standard,
    };
}
