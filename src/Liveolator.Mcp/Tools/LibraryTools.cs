using System.ComponentModel;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Mcp.Contracts;
using Liveolator.Mcp.Session;
using ModelContextProtocol.Server;

namespace Liveolator.Mcp.Tools;

/// <summary>MCP tools for building and querying the music catalog.</summary>
[McpServerToolType]
public sealed class LibraryTools
{
    [McpServerTool(Name = "scan_music_folders")]
    [Description("Scan one or more folders (recursively) for audio files and analyze each: BPM, " +
                 "musical key + Camelot code, intro/outro cues, and duration. Incremental — unchanged " +
                 "files are not re-analyzed, and results are cached for fast later calls. WAV needs no " +
                 "setup; mp3/flac/m4a/ogg/opus need the FFmpeg libraries installed (otherwise those " +
                 "files appear under 'failures' with an actionable message).")]
    public static async Task<ScanSummary> ScanMusicFolders(
        LibrarySession session,
        [Description("Absolute folder paths to scan, searched recursively.")] string[] folders,
        [Description("Re-analyze every file even if already cached. Default false.")] bool force = false,
        CancellationToken cancellationToken = default)
        => await session.ScanAsync(folders, force, cancellationToken).ConfigureAwait(false);

    [McpServerTool(Name = "list_tracks")]
    [Description("List catalogued tracks with optional filtering, sorting and paging. Returns the " +
                 "analysis for each track. Run scan_music_folders first to populate the catalog.")]
    public static async Task<IReadOnlyList<TrackInfo>> ListTracks(
        LibrarySession session,
        [Description("Filter by status: Ok, PartiallyAnalyzed, or Failed. Omit for all.")] string? status = null,
        [Description("Only tracks with BPM ≥ this value.")] double? minBpm = null,
        [Description("Only tracks with BPM ≤ this value.")] double? maxBpm = null,
        [Description("Only tracks in this Camelot key (e.g. '8B').")] string? camelot = null,
        [Description("Sort by: title, bpm, or key. Default title.")] string sort = "title",
        [Description("Max results to return. Default 100.")] int limit = 100,
        [Description("Number of results to skip (for paging). Default 0.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MusicTrack> all = await session.SnapshotAsync(cancellationToken).ConfigureAwait(false);

        IEnumerable<MusicTrack> query = all;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse(status, ignoreCase: true, out MediaAnalysisStatus parsed))
                throw new ArgumentException($"Unknown status '{status}'. Use Ok, PartiallyAnalyzed, or Failed.");
            query = query.Where(t => t.Status == parsed);
        }
        if (minBpm is { } lo) query = query.Where(t => t.Bpm is not null && t.Bpm.Bpm >= lo);
        if (maxBpm is { } hi) query = query.Where(t => t.Bpm is not null && t.Bpm.Bpm <= hi);
        if (!string.IsNullOrWhiteSpace(camelot))
            query = query.Where(t => string.Equals(t.Key?.Camelot, camelot, StringComparison.OrdinalIgnoreCase));

        query = sort.ToLowerInvariant() switch
        {
            "bpm" => query.OrderByDescending(t => t.Bpm?.Bpm ?? double.MinValue),
            "key" => query.OrderBy(t => t.Key?.Camelot ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            "title" => query.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase),
            _ => throw new ArgumentException($"Unknown sort '{sort}'. Use title, bpm, or key.")
        };

        return query
            .Skip(Math.Max(0, offset))
            .Take(Math.Clamp(limit, 1, 1000))
            .Select(TrackInfo.From)
            .ToList();
    }

    [McpServerTool(Name = "get_track")]
    [Description("Get the full analysis for one catalogued track by its exact file path.")]
    public static async Task<TrackInfo> GetTrack(
        LibrarySession session,
        [Description("Exact file path of the track (as catalogued).")] string path,
        CancellationToken cancellationToken = default)
    {
        MusicTrack? track = await session.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (track is null)
            throw new ArgumentException($"No catalogued track at '{path}'. Scan its folder first, or check the path.");
        return TrackInfo.From(track);
    }

    [McpServerTool(Name = "get_catalog_stats")]
    [Description("Summarize the catalog: track counts by status, average BPM, key distribution, and a " +
                 "10-BPM-bucket tempo histogram — a quick way to understand a music collection.")]
    public static async Task<CatalogStats> GetCatalogStats(
        LibrarySession session, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MusicTrack> all = await session.SnapshotAsync(cancellationToken).ConfigureAwait(false);

        double[] bpms = all.Where(t => t.Bpm is not null).Select(t => t.Bpm!.Bpm).ToArray();

        var keyDistribution = all
            .Where(t => t.Key is not null)
            .GroupBy(t => t.Key!.Camelot)
            .OrderByDescending(g => g.Count())
            .Select(g => new KeyCount(g.Key, g.Count()))
            .ToList();

        var histogram = bpms
            .GroupBy(b => (int)(b / 10) * 10)
            .OrderBy(g => g.Key)
            .Select(g => new BpmBucket($"{g.Key}-{g.Key + 9}", g.Count()))
            .ToList();

        return new CatalogStats(
            Total: all.Count,
            Ok: all.Count(t => t.Status == MediaAnalysisStatus.Ok),
            PartiallyAnalyzed: all.Count(t => t.Status == MediaAnalysisStatus.PartiallyAnalyzed),
            Failed: all.Count(t => t.Status == MediaAnalysisStatus.Failed),
            AverageBpm: bpms.Length > 0 ? Math.Round(bpms.Average(), 1) : null,
            KeyDistribution: keyDistribution,
            BpmHistogram: histogram);
    }
}
