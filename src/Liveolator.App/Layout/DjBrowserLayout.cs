namespace Liveolator.App.Layout;

/// <summary>
/// The DJ-tab console↔browser vertical split per size tier, as a star weight for the browser row relative
/// to the console row (which is always 1*). 0 = the browser is collapsed and the console takes the whole
/// tab. A PROPORTIONAL split (not a fixed band) so it always fits the actual screen height: a fixed band
/// overflowed short wide screens (e.g. 1920×1080) and crushed the console.
///
/// Laptop tiers collapse the browser (the console needs the height to stay one-screen; digging happens on
/// the LIBRARIES tab). Wide/Ultra open a generous Rekordbox-style bottom band that fills the otherwise-empty
/// space on a big screen.
/// </summary>
public static class DjBrowserLayout
{
    public static double RowShare(LayoutSizeClass cls) => cls switch
    {
        LayoutSizeClass.Compact => 0.0,
        LayoutSizeClass.Standard => 0.0,
        LayoutSizeClass.Wide => 0.55,   // ~35% of the tab height
        LayoutSizeClass.Ultra => 0.72,  // ~42% of the tab height
        _ => 0.0,
    };
}
