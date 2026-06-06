namespace Liveolator.Core.Library.Music;

/// <summary>The distinct facet values present in a catalog, for populating filter dropdowns.</summary>
public sealed record TrackFacets(
    IReadOnlyList<string> Artists,
    IReadOnlyList<string> Genres,
    IReadOnlyList<int> Years,
    IReadOnlyList<string> FileTypes)
{
    public static TrackFacets Empty { get; } =
        new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<int>(), Array.Empty<string>());

    /// <summary>
    /// Computes the distinct, sorted facet values over <paramref name="tracks"/> (blanks/nulls dropped).
    /// Pure — drives the Libraries facet dropdowns from the same catalog the table shows.
    /// </summary>
    public static TrackFacets Of(IEnumerable<MusicTrack> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        var list = tracks as ICollection<MusicTrack> ?? tracks.ToList();

        IReadOnlyList<string> artists = Distinct(list.Select(t => t.Artist));
        IReadOnlyList<string> genres = Distinct(list.Select(t => t.Metadata?.Genre));
        IReadOnlyList<string> fileTypes = Distinct(list.Select(t => t.FileType));
        IReadOnlyList<int> years = list
            .Select(t => t.Metadata?.Year)
            .Where(y => y is > 0)
            .Select(y => y!.Value)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();

        return new TrackFacets(artists, genres, years, fileTypes);
    }

    private static IReadOnlyList<string> Distinct(IEnumerable<string?> values)
        => values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
