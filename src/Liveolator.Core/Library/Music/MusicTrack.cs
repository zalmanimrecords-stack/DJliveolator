using System.IO;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;

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
    bool AnalysisIsManual = false) : IMediaEntry
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
