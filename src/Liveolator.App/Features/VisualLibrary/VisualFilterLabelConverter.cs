using System.Globalization;
using Avalonia.Data.Converters;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Visual;

namespace Liveolator.App.Features.VisualLibrary;

/// <summary>
/// Display labels for the Visual Library filter dropdown items: a friendly word for each
/// <see cref="VisualMediaKind"/> and <see cref="MediaAnalysisStatus"/>, with "Any kind"/"Any status"
/// for the null sentinel entries. Presentation only — keeps the enum→text mapping out of the markup.
/// </summary>
public sealed class VisualFilterLabelConverter : IValueConverter
{
    public static readonly VisualFilterLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            VisualMediaKind.Image => "Images",
            VisualMediaKind.Video => "Videos",
            MediaAnalysisStatus.Ok => "OK",
            MediaAnalysisStatus.PartiallyAnalyzed => "Partial",
            MediaAnalysisStatus.Failed => "Failed",
            // A null entry is the "all" sentinel; its meaning depends on which dropdown it leads.
            null when string.Equals(parameter as string, "kind", StringComparison.Ordinal) => "Any kind",
            null => "Any status",
            _ => value.ToString() ?? string.Empty,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
