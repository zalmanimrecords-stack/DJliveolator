using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Liveolator.Core.Library;

namespace Liveolator.App.Features.Libraries;

/// <summary>Maps a <see cref="MediaAnalysisStatus"/> to one of the theme brushes for the row dot.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public static readonly StatusToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value is MediaAnalysisStatus status
            ? status switch
            {
                MediaAnalysisStatus.Ok => "Dim",
                MediaAnalysisStatus.PartiallyAnalyzed => "Accent",
                _ => "Red",
            }
            : "Faint";

        if (Application.Current?.Resources.TryGetResource(key, null, out object? resource) == true
            && resource is IBrush brush)
            return brush;

        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
