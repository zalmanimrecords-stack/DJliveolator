using Avalonia.Media;

namespace Liveolator.App.Controls;

/// <summary>
/// Shared drawing helpers for the custom skeuomorphic controls (<see cref="Knob"/>, <see cref="Fader"/>,
/// <see cref="Jog"/>), which all render glows/halos by re-alpha'ing an accent brush.
/// </summary>
internal static class ControlBrush
{
    /// <summary>
    /// Returns <paramref name="source"/> re-alpha'd to <paramref name="opacity"/> (0..1) when it is a solid
    /// colour brush; non-solid brushes (e.g. gradients) are returned unchanged.
    /// </summary>
    internal static IBrush Halo(IBrush source, double opacity)
    {
        if (source is ISolidColorBrush solid)
        {
            Color color = solid.Color;
            return new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B));
        }

        return source;
    }
}
