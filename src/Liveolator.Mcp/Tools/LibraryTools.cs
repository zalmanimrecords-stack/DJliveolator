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
    [Description("Scan folders recursively for audio files, analyze them, and persist the catalog. " +
                 "The scan is incremental unless force is true. WAV works without external setup; " +
                 "compressed formats require FFmpeg.")]
    public static Task<ScanSummary> ScanMusicFolders(
        LibrarySession session,
        [Description("Absolute folder paths to scan recursively.")] string[] folders,
        [Description("Re-analyze every file even if it is cached.")] bool force = false,
        CancellationToken cancellationToken = default)
        => session.ScanAsync(folders, force, cancellationToken);

    [McpServerTool(Name = "list_tracks")]
    [Description("Query catalogued tracks with metadata, analysis, filtering, sorting, and paging. " +
                 "Run scan_music_folders first. TrackInfo includes tag metadata, media kind, analyzer " +
                 "version, and whether analysis was manually corrected.")]
    public static async Task<IReadOnlyList<TrackInfo>> ListTracks(
        LibrarySession session,
        [Description("Text matched against title, artist, or file name.")] string? text = null,
        [Description("Filter by media kind: Track or Sample.")] string? kind = null,
        [Description("Exact artist tag to match.")] string? artist = null,
        [Description("Exact genre tag to match.")] string? genre = null,
        [Description("Filter by status: Ok, PartiallyAnalyzed, or Failed.")] string? status = null,
        [Description("Only tracks with BPM at least this value.")] double? minBpm = null,
        [Description("Only tracks with BPM at most this value.")] double? maxBpm = null,
        [Description("Only tracks in this Camelot key.")] string? camelot = null,
        [Description("Exact release year to match.")] int? year = null,
        [Description("File extension without a dot, such as mp3 or wav.")] string? fileType = null,
        [Description("Minimum duration in seconds. Unknown durations are retained.")] double? minDurationSeconds = null,
        [Description("Sort by title, bpm, key, or duration.")] string sort = "title",
        [Description("Reverse the selected sort order. Missing values remain last.")] bool descending = false,
        [Description("Maximum results to return.")] int limit = 100,
        [Description("Number of results to skip.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MusicTrack> all = await session.SnapshotAsync(cancellationToken).ConfigureAwait(false);

        MusicMediaKind? parsedKind = ParseOptionalEnum<MusicMediaKind>(kind, "kind", "Track or Sample");
        MediaAnalysisStatus? parsedStatus = ParseOptionalEnum<MediaAnalysisStatus>(
            status, "status", "Ok, PartiallyAnalyzed, or Failed");

        if (minDurationSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(minDurationSeconds), "Minimum duration cannot be negative.");

        TrackSortKey sortKey = sort.ToLowerInvariant() switch
        {
            "bpm" => TrackSortKey.Bpm,
            "key" => TrackSortKey.Key,
            "duration" => TrackSortKey.Duration,
            "title" => TrackSortKey.Title,
            _ => throw new ArgumentException($"Unknown sort '{sort}'. Use title, bpm, key, or duration.")
        };

        var filter = new TrackFilter(
            Text: text,
            Kind: parsedKind,
            Artist: artist,
            Genre: genre,
            MinBpm: minBpm,
            MaxBpm: maxBpm,
            Camelot: camelot,
            Year: year,
            FileType: fileType,
            Status: parsedStatus,
            MinDuration: minDurationSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null);

        return TrackQuery.Query(all, filter, sortKey, descending, limit, offset)
            .Select(TrackInfo.From)
            .ToList();
    }

    [McpServerTool(Name = "get_track")]
    [Description("Get the full analysis and metadata for one catalogued track by exact path.")]
    public static async Task<TrackInfo> GetTrack(
        LibrarySession session,
        [Description("Exact file path of the track as catalogued.")] string path,
        CancellationToken cancellationToken = default)
    {
        MusicTrack? track = await session.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (track is null)
            throw MissingTrack(path);
        return TrackInfo.From(track);
    }

    [McpServerTool(Name = "get_catalog_stats")]
    [Description("Summarize track status, average BPM, key distribution, and tempo histogram.")]
    public static async Task<CatalogStats> GetCatalogStats(
        LibrarySession session,
        CancellationToken cancellationToken = default)
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
            all.Count,
            all.Count(t => t.Status == MediaAnalysisStatus.Ok),
            all.Count(t => t.Status == MediaAnalysisStatus.PartiallyAnalyzed),
            all.Count(t => t.Status == MediaAnalysisStatus.Failed),
            bpms.Length > 0 ? Math.Round(bpms.Average(), 1) : null,
            keyDistribution,
            histogram);
    }

    [McpServerTool(Name = "reanalyze_track")]
    [Description("Re-run local BPM, key, duration, and cue analysis for one catalogued track and " +
                 "persist the result. Without force, only stale, failed, or incomplete analysis is retried.")]
    public static async Task<TrackInfo> ReanalyzeTrack(
        LibrarySession session,
        [Description("Exact path of a catalogued track.")] string path,
        [Description("Replace even current or manually corrected analysis.")] bool force = false,
        CancellationToken cancellationToken = default)
    {
        MusicTrack? track = await session.ReanalyzeAsync(path, force, cancellationToken).ConfigureAwait(false);
        if (track is null)
            throw MissingTrack(path);
        return TrackInfo.From(track);
    }

    [McpServerTool(Name = "reanalyze_pending_tracks")]
    [Description("Re-analyze all failed, incomplete, or old-version catalog entries. Manual " +
                 "corrections are preserved and progress is persisted incrementally.")]
    public static Task<ReanalysisSummary> ReanalyzePendingTracks(
        LibrarySession session,
        CancellationToken cancellationToken = default)
        => session.ReanalyzePendingAsync(cancellationToken);

    private static TEnum? ParseOptionalEnum<TEnum>(string? value, string name, string valid)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (Enum.TryParse(value, ignoreCase: true, out TEnum parsed))
            return parsed;
        throw new ArgumentException($"Unknown {name} '{value}'. Use {valid}.");
    }

    private static ArgumentException MissingTrack(string path)
        => new($"No catalogued track at '{path}'. Scan its folder first, or check the path.");
}
