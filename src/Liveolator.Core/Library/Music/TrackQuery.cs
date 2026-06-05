using System.IO;

namespace Liveolator.Core.Library.Music;

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
    {
        ArgumentNullException.ThrowIfNull(tracks);

        IEnumerable<MusicTrack> query = tracks;

        if (!string.IsNullOrWhiteSpace(text))
        {
            string needle = text.Trim();
            query = query.Where(t => MatchesText(t, needle));
        }

        if (minBpm is { } lo)
            query = query.Where(t => t.Bpm is not null && t.Bpm.Bpm >= lo);
        if (maxBpm is { } hi)
            query = query.Where(t => t.Bpm is not null && t.Bpm.Bpm <= hi);

        if (!string.IsNullOrWhiteSpace(camelot))
        {
            string key = camelot.Trim();
            query = query.Where(t => string.Equals(t.Key?.Camelot, key, StringComparison.OrdinalIgnoreCase));
        }

        return query
            .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, MaxResults))
            .ToList();
    }

    private static bool MatchesText(MusicTrack track, string needle)
        => Contains(track.Title, needle)
           || Contains(track.Artist, needle)
           || Contains(Path.GetFileName(track.File.Path), needle);

    private static bool Contains(string? haystack, string needle)
        => haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
