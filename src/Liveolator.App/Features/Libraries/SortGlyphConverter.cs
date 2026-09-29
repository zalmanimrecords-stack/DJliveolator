using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Liveolator.Core.Library.Music;

namespace Liveolator.App.Features.Libraries;

/// <summary>
/// Direction glyph for a clickable column header: "▲"/"▼" when this column (the <c>ConverterParameter</c>
/// <see cref="TrackSortKey"/>) is the active <see cref="LibrariesViewModel.SortKey"/>, blank otherwise.
/// A <c>MultiBinding</c> of <c>SortKey</c> + <c>SortDescending</c>, so both drive the glyph the same way
/// the sort ComboBox + direction toggle already do.
/// </summary>
public sealed class SortGlyphConverter : IMultiValueConverter
{
    public static readonly SortGlyphConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [TrackSortKey activeKey, bool descending] || parameter is not TrackSortKey column)
            return string.Empty;

        if (activeKey != column)
            return string.Empty;

        return descending ? "▼" : "▲";
    }
}
