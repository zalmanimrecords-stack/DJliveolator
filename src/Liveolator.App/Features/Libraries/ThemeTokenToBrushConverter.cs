using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Liveolator.App.Features.Libraries;

/// <summary>
/// Maps a theme-token NAME supplied by the view-model (e.g. "Red"/"Accent"/"Faint") to the theme brush.
/// Complements <see cref="PresenceToBrushConverter"/> (boolean → fixed token) for badges that need more
/// than two states, e.g. the BPM badge's missing / present / conflicted. Theme tokens only — no
/// hardcoded hexes (App module iron rule #5).
/// </summary>
public sealed class ThemeTokenToBrushConverter : IValueConverter
{
    public static readonly ThemeTokenToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value as string ?? "Faint";

        if (Application.Current?.Resources.TryGetResource(key, null, out object? resource) == true
            && resource is IBrush brush)
            return brush;

        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
