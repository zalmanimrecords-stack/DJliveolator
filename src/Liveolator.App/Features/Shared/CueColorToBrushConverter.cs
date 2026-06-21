using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Liveolator.App.Features.Shared;

/// <summary>
/// Maps a stored hot-cue color (a nullable 0xRRGGBB int) to a brush for a cue swatch or pad accent. An
/// explicit color renders verbatim; an unset color falls back to the theme <c>Accent</c> brush so every
/// cue still shows a color without hardcoding a hex (App rule #5 — theme from tokens). Shared by the
/// Libraries cue list and the Live deck hot-cue pads.
/// </summary>
public sealed class CueColorToBrushConverter : IValueConverter
{
    public static readonly CueColorToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int rgb)
        {
            var color = Color.FromRgb((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
            return new ImmutableSolidColorBrush(color);
        }

        if (Application.Current?.Resources.TryGetResource("Accent", null, out object? resource) == true
            && resource is IBrush brush)
            return brush;

        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
