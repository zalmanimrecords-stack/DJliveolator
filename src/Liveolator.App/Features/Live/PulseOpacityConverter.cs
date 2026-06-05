using System.Globalization;
using Avalonia.Data.Converters;

namespace Liveolator.App.Features.Live;

/// <summary>
/// Maps the clock's <c>IsBeat</c>/<c>IsDownbeat</c> flag to an opacity so the pulse indicator flashes
/// bright on a boundary and dims between. The render-loop timer republishes state every frame, so the
/// flag clears on the next tick and the element visibly blinks. Kept as a converter (not VM state) so
/// the flash is a pure presentation concern.
/// </summary>
public sealed class PulseOpacityConverter : IValueConverter
{
    private const double Lit = 1.0;
    private const double Dim = 0.18;

    public static PulseOpacityConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Lit : Dim;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
