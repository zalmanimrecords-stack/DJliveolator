using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Liveolator.App.Features.Libraries;

/// <summary>
/// Maps a presence boolean to a theme brush for a per-track analysis badge: lit (the token named by
/// <c>ConverterParameter</c>, e.g. "Accent"/"Green"/"Amber") when present, dim ("Faint") when absent.
/// Theme tokens only — no hardcoded hexes (App module iron rule #5).
/// </summary>
public sealed class PresenceToBrushConverter : IValueConverter
{
    public static readonly PresenceToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool present = value is true;
        string key = present ? parameter as string ?? "Accent" : "Faint";

        if (Application.Current?.Resources.TryGetResource(key, null, out object? resource) == true
            && resource is IBrush brush)
            return brush;

        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
