using System.Globalization;
using Avalonia.Data.Converters;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;

namespace Liveolator.App.Features.Libraries;

/// <summary>
/// Display labels for the Libraries filter/sort dropdown items: a friendly word for each
/// <see cref="MediaAnalysisStatus"/> and <see cref="TrackSortKey"/>, and "Any status" for a null
/// status entry. Presentation only — keeps the enum→text mapping out of the markup.
/// </summary>
public sealed class FilterLabelConverter : IValueConverter
{
    public static readonly FilterLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            null => "Any status",
            MediaAnalysisStatus.Ok => "OK",
            MediaAnalysisStatus.PartiallyAnalyzed => "Partial",
            MediaAnalysisStatus.Failed => "Failed",
            TrackSortKey.Title => "Title",
            TrackSortKey.Bpm => "BPM",
            TrackSortKey.Key => "Key",
            TrackSortKey.Duration => "Duration",
            _ => value.ToString() ?? string.Empty,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
