using System.ComponentModel;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Import;
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
                 "Only the folders passed are walked — tracks catalogued from other folders are left " +
                 "untouched, and force re-analyzes only what is under these folders. The scan is " +
                 "incremental unless force is true. WAV works without external setup; compressed " +
                 "formats require FFmpeg.")]
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

    [McpServerTool(Name = "set_track_analysis")]
    [Description("Correct a catalogued track's BPM and/or musical key by hand, and lock that track " +
                 "against automatic re-analysis so the correction survives every later scan. Use it when " +
                 "the detector is confidently wrong — a published tempo the analysis missed, or a key you " +
                 "verified. Whichever value you omit keeps what analysis found, so a wrong tempo can be " +
                 "fixed without asserting a key you have not checked. Only reanalyze_track(force: true) " +
                 "overwrites the result.")]
    public static async Task<TrackInfo> SetTrackAnalysis(
        LibrarySession session,
        [Description("Exact path of a catalogued track.")] string path,
        [Description("Corrected tempo in BPM. Omit to keep the analyzed tempo.")] double? bpm = null,
        [Description("Corrected key as a Camelot code, 1A to 12B. Omit to keep the analyzed key.")] string? key = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        MusicTrack? track = await session
            .SetManualAnalysisAsync(path, bpm, key, cancellationToken)
            .ConfigureAwait(false);
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

    [McpServerTool(Name = "measure_catalog_loudness")]
    [Description("Measure the integrated loudness (LUFS) of every catalogued track that lacks it, so " +
                 "build_dj_set can gain each clip to one level instead of playing every master at unity — " +
                 "without which a set steps up and down in volume at every transition and the crossfades " +
                 "lurch. Independent of BPM/key analysis, so it never triggers a re-analysis and it also " +
                 "covers hand-corrected tracks. Requires FFmpeg. Progress is persisted incrementally, so " +
                 "the pass is resumable and safe to re-run; an unreachable or silent file is simply left " +
                 "unmeasured and picked up next time.")]
    public static Task<LoudnessSummary> MeasureCatalogLoudness(
        LibrarySession session,
        CancellationToken cancellationToken = default)
        => session.MeasureLoudnessAsync(cancellationToken);

    [McpServerTool(Name = "import_library")]
    [Description("Import tracks, hot cues, beat grids, key, and playlists from another DJ app's library " +
                 "into the catalog, then persist. Formats: Rekordbox, Traktor, VirtualDJ (pass the exported " +
                 "library file) or Serato, Mixxx (pass the library folder). Non-destructive by default " +
                 "(fills only missing analysis and keeps existing cues); set overwrite to let the source win.")]
    public static Task<ImportSummaryDto> ImportLibrary(
        LibrarySession session,
        [Description("Source app: Rekordbox, Traktor, VirtualDJ, Serato, or Mixxx.")] string format,
        [Description("The exported library file (Rekordbox/Traktor/VirtualDJ) or the library folder " +
                     "(Serato library root / the folder holding mixxxdb.sqlite).")] string path,
        [Description("Overwrite existing BPM/key/cues instead of only filling gaps.")] bool overwrite = false,
        CancellationToken cancellationToken = default)
        => session.ImportAsync(
            format, path,
            overwrite ? ImportMergePolicy.Overwrite : ImportMergePolicy.FillGaps,
            cancellationToken);

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
