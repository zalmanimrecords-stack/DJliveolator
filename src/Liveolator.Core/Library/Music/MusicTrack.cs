using System.IO;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Analysis.Structure;
using Liveolator.Core.Enrichment;

namespace Liveolator.Core.Library.Music;

/// <summary>A catalogued music file with its tag metadata and offline analysis (BPM, key/scale, duration, cues).</summary>
public sealed record MusicTrack(
    ScannedFile File,
    BpmResult? Bpm,
    MusicalKey? Key,
    TimeSpan? Duration,
    TrackCues Cues,
    MediaAnalysisStatus Status,
    string? Error,
    TrackMetadata? Metadata = null,
    MusicMediaKind Kind = MusicMediaKind.Track,
    int AnalyzerVersion = 0,
    bool AnalysisIsManual = false,
    // Optional offline song-structure segmentation (doc 32). Null when not yet analyzed; an older
    // catalog JSON simply lacks the property and deserializes to null (backward compatible — the
    // snapshot version is NOT bumped, so existing caches still load).
    SongStructure? Structure = null,
    // Library-management fields (the prepare workflow). All added the same backward-compatible way as
    // Structure — optional, so an older cache defaults them and existing catalogs still load with no
    // schema bump. They are USER/library data, not analysis, so a re-decode must preserve them.
    int Rating = 0,                 // 0 = unrated, 1–5 stars
    DateTime? DateAdded = null,     // when the track first entered the catalog
    DateTime? LastPlayed = null,    // when it was last loaded to a deck
    int PlayCount = 0,
    // Online BPM cross-check (doc 16), added the same backward-compatible way — optional, no schema
    // bump. OnlineBpm/Source keep the raw online value for display; BpmProvenance is the merged verdict
    // (Conflicted = the library's visible flag); OnlineLookupUtc marks a COMPLETED lookup (hit or miss)
    // so the free API is never re-queried for the same track.
    double? OnlineBpm = null,
    string? OnlineBpmSource = null,
    BpmProvenance BpmProvenance = BpmProvenance.Unknown,
    DateTime? OnlineLookupUtc = null,
    // When offline analysis last ran for this track — the "last scanned" stamp shown in the library.
    // Added the same backward-compatible way as the fields above: optional, so an older cache defaults
    // it to null and still loads, no schema bump.
    DateTime? LastAnalyzedUtc = null) : IMediaEntry
{
    /// <summary>Display title: the tag title when present, otherwise derived from the file name.</summary>
    public string Title =>
        string.IsNullOrWhiteSpace(Metadata?.Title)
            ? Path.GetFileNameWithoutExtension(File.Path)
            : Metadata!.Title!;

    /// <summary>Track artist from tags, or null when untagged.</summary>
    public string? Artist => string.IsNullOrWhiteSpace(Metadata?.Artist) ? null : Metadata!.Artist;

    /// <summary>Lower-case file extension without the dot (e.g. "mp3"), for the file-type filter; null if none.</summary>
    public string? FileType
    {
        get
        {
            string ext = Path.GetExtension(File.Path);
            return ext.Length > 1 ? ext[1..].ToLowerInvariant() : null;
        }
    }
}
