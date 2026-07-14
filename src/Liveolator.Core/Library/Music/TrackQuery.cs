using System.IO;

namespace Liveolator.Core.Library.Music;

/// <summary>
/// A composable set of library filters. Every field is optional; a null/blank field matches all, so
/// the facets combine (logical AND). Drives both the Libraries filter bar and the MCP <c>list_tracks</c>
/// tool from one tested place.
/// </summary>
public sealed record TrackFilter(
    string? Text = null,
    MusicMediaKind? Kind = null,
    string? Artist = null,
    string? Genre = null,
    double? MinBpm = null,
    double? MaxBpm = null,
    string? Camelot = null,
    int? Year = null,
    string? FileType = null,
    MediaAnalysisStatus? Status = null,
    TimeSpan? MinDuration = null);

/// <summary>
/// Pure, reusable filtering of a catalogued track set — free text (title / artist / file name),
/// BPM range, and Camelot key — with deterministic ordering. The single place this query logic
/// lives, so the MCP server (doc 17) and any UI search share one tested implementation rather than
/// re-deriving LINQ. IO-free, so it unit-tests over an in-memory set.
/// </summary>
public static class TrackQuery
{
    /// <summary>Upper bound on returned results, mirroring the catalog tools' cap.</summary>
    public const int MaxResults = 1000;

    /// <summary>
    /// Returns the tracks matching every supplied filter, ordered by title for stable output and
    /// capped at <paramref name="limit"/>.
    /// </summary>
    /// <param name="tracks">The set to query.</param>
    /// <param name="text">Case-insensitive substring matched against title, artist, or file name; omit to match all.</param>
    /// <param name="minBpm">Keep tracks with BPM ≥ this (tracks without a BPM are excluded when set).</param>
    /// <param name="maxBpm">Keep tracks with BPM ≤ this (tracks without a BPM are excluded when set).</param>
    /// <param name="camelot">Keep tracks in this Camelot key (exact, case-insensitive).</param>
    /// <param name="limit">Max results, clamped to 1..<see cref="MaxResults"/>.</param>
    public static IReadOnlyList<MusicTrack> Search(
        IEnumerable<MusicTrack> tracks,
        string? text = null,
        double? minBpm = null,
        double? maxBpm = null,
        string? camelot = null,
        int limit = 100)
        => Apply(tracks, new TrackFilter(Text: text, MinBpm: minBpm, MaxBpm: maxBpm, Camelot: camelot), limit);

    /// <summary>
    /// Returns the tracks matching every supplied facet of <paramref name="filter"/> (null/blank facets
    /// match all), ordered by title and capped at <paramref name="limit"/>.
    /// </summary>
    public static IReadOnlyList<MusicTrack> Apply(
        IEnumerable<MusicTrack> tracks, TrackFilter filter, int limit = 100)
        => Query(tracks, filter, TrackSortKey.Title, descending: false, limit);

    /// <summary>
    /// Filters, sorts, and pages a catalog query in one deterministic operation.
    /// </summary>
    public static IReadOnlyList<MusicTrack> Query(
        IEnumerable<MusicTrack> tracks,
        TrackFilter filter,
        TrackSortKey sortKey = TrackSortKey.Title,
        bool descending = false,
        int limit = 100,
        int offset = 0)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(filter);

        IEnumerable<MusicTrack> query = tracks;

        if (filter.Kind is { } kind)
            query = query.Where(t => t.Kind == kind);

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            // Multi-term AND: each whitespace-separated term must match some field, so "house 124" finds
            // house tracks around 124 BPM. A single term behaves exactly as a plain substring search.
            string[] terms = filter.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            query = query.Where(t => terms.All(term => MatchesTerm(t, term)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Artist))
            query = query.Where(t => string.Equals(t.Artist, filter.Artist, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filter.Genre))
            query = query.Where(t => string.Equals(t.Metadata?.Genre, filter.Genre, StringComparison.OrdinalIgnoreCase));

        if (filter.MinBpm is { } lo)
            query = query.Where(t => t.Bpm is not null && t.Bpm.Bpm >= lo);
        if (filter.MaxBpm is { } hi)
            query = query.Where(t => t.Bpm is not null && t.Bpm.Bpm <= hi);

        if (!string.IsNullOrWhiteSpace(filter.Camelot))
        {
            string key = filter.Camelot.Trim();
            query = query.Where(t => string.Equals(t.Key?.Camelot, key, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.Year is { } year)
            query = query.Where(t => t.Metadata?.Year == year);
        if (!string.IsNullOrWhiteSpace(filter.FileType))
            query = query.Where(t => string.Equals(t.FileType, filter.FileType, StringComparison.OrdinalIgnoreCase));
        if (filter.Status is { } status)
            query = query.Where(t => t.Status == status);
        if (filter.MinDuration is { } minDuration)
            query = query.Where(t => t.Duration is null || t.Duration >= minDuration);

        return TrackSort.Apply(query, sortKey, descending)
            .Skip(Math.Max(0, offset))
            .Take(Math.Clamp(limit, 1, MaxResults))
            .ToList();
    }

    // One search term matches a track if it is a substring of any searchable field: title, artist, file
    // name, album, genre, comment/notes, Camelot key, or the whole-number BPM (so "124" finds ~124 BPM).
    private static bool MatchesTerm(MusicTrack track, string term)
        => Contains(track.Title, term)
           || Contains(track.Artist, term)
           || Contains(Path.GetFileName(track.File.Path), term)
           || Contains(track.Metadata?.Album, term)
           || Contains(track.Metadata?.Genre, term)
           || Contains(track.Metadata?.Comment, term)
           || Contains(track.Key?.Camelot, term)
           || (track.Bpm is { } bpm && Contains(bpm.Bpm.ToString("0"), term));

    private static bool Contains(string? haystack, string needle)
        => haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
