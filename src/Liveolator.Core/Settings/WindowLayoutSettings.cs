namespace Liveolator.Core.Settings;

/// <summary>
/// Persisted main-window layout (doc 12): which tab to reopen, the window size + position, and whether
/// it was full-screen — so the app returns to where the performer left it instead of always opening on
/// the first tab, full-screen, at the default size. Pure data, persisted via <c>ISettingsStore</c>.
/// <see cref="Normalized"/> clamps the size to the window's minimum and folds NaN/garbage values, so a
/// stale or hand-edited config can never produce an unusable (tiny / NaN) window.
/// </summary>
/// <param name="ActiveTabId">Stable id (the tab's label) of the tab to reopen; null = the first tab.</param>
/// <param name="Width">Window width in DIPs.</param>
/// <param name="Height">Window height in DIPs.</param>
/// <param name="X">Window left in screen pixels; null = let the OS place it (centred on first run).</param>
/// <param name="Y">Window top in screen pixels; null = let the OS place it.</param>
/// <param name="IsFullScreen">Whether the window was full-screen (the app's launch default).</param>
// The size parameter defaults are literals (a primary-ctor parameter default cannot reference a
// body-declared const); they MUST equal DefaultWidth / DefaultHeight below.
public sealed record WindowLayoutSettings(
    string? ActiveTabId = null,
    double Width = 1280,
    double Height = 800,
    double? X = null,
    double? Y = null,
    bool IsFullScreen = true)
{
    /// <summary>Smallest restorable window width (matches the window's MinWidth).</summary>
    public const double MinWidth = 960;

    /// <summary>Smallest restorable window height (matches the window's MinHeight).</summary>
    public const double MinHeight = 600;

    /// <summary>Default window width on first run.</summary>
    public const double DefaultWidth = 1280;

    /// <summary>Default window height on first run.</summary>
    public const double DefaultHeight = 800;

    /// <summary>The default layout: first tab, default size, full-screen (the app's launch default).</summary>
    public static WindowLayoutSettings Default { get; } = new();

    /// <summary>
    /// Returns a copy with a blank tab id folded to null, the size clamped to its minimum (NaN/non-positive
    /// → default), and any NaN/infinite position dropped — so a stale or hand-edited config can never
    /// produce an unusable window.
    /// </summary>
    public WindowLayoutSettings Normalized()
        => this with
        {
            ActiveTabId = string.IsNullOrWhiteSpace(ActiveTabId) ? null : ActiveTabId,
            Width = NormalizeSize(Width, DefaultWidth, MinWidth),
            Height = NormalizeSize(Height, DefaultHeight, MinHeight),
            X = NormalizeCoordinate(X),
            Y = NormalizeCoordinate(Y),
        };

    private static double NormalizeSize(double value, double fallback, double minimum)
        => double.IsNaN(value) || value <= 0 ? fallback : Math.Max(value, minimum);

    private static double? NormalizeCoordinate(double? value)
        => value is { } v && !double.IsNaN(v) && !double.IsInfinity(v) ? v : null;
}
