using System.IO;

namespace Liveolator.Core.Library.Visual;

/// <summary>
/// A composable set of visual-asset filters. Every field is optional; a null/blank field matches all,
/// so the facets combine (logical AND). Drives both the Visual Library filter bar and (later) the MCP
/// <c>list_visuals</c> tool from one tested place.
/// </summary>
public sealed record VisualAssetFilter(
    string? Text = null,
    VisualMediaKind? Kind = null,
    MediaAnalysisStatus? Status = null);

/// <summary>
/// Pure, reusable filtering of a catalogued visual-asset set — free text (title / file name) plus a
/// kind (Image/Video) and analysis-status facet — with deterministic title ordering. IO-free, so it
/// unit-tests over an in-memory set. Mirrors <see cref="Music.TrackQuery"/> for the visual domain so
/// the browser UI and any agent tool share one tested query rather than re-deriving LINQ.
/// </summary>
public static class VisualAssetQuery
{
    /// <summary>Upper bound on returned results, mirroring the catalog tools' cap.</summary>
    public const int MaxResults = 5000;

    /// <summary>
    /// Returns the assets matching every supplied facet of <paramref name="filter"/> (null/blank facets
    /// match all), ordered by title and capped at <paramref name="limit"/>.
    /// </summary>
    public static IReadOnlyList<VisualAsset> Apply(
        IEnumerable<VisualAsset> assets, VisualAssetFilter filter, int limit = MaxResults)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(filter);

        IEnumerable<VisualAsset> query = assets;

        if (filter.Kind is { } kind)
            query = query.Where(a => a.Kind == kind);

        if (filter.Status is { } status)
            query = query.Where(a => a.Status == status);

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            string needle = filter.Text.Trim();
            query = query.Where(a => MatchesText(a, needle));
        }

        return query
            .OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, MaxResults))
            .ToList();
    }

    private static bool MatchesText(VisualAsset asset, string needle)
        => Contains(asset.Title, needle)
           || Contains(Path.GetFileName(asset.File.Path), needle);

    private static bool Contains(string? haystack, string needle)
        => haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
