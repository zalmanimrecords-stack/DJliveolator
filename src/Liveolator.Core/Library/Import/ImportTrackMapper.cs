using System;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Library.Import;

/// <summary>
/// Builds or merges a <see cref="MusicTrack"/> from a source track. A new track is created in full; an
/// existing one is merged per <see cref="ImportMergePolicy"/> (FillGaps fills only missing fields;
/// Overwrite lets the source win). Imported tracks are marked <see cref="MusicTrack.AnalysisIsManual"/>
/// so a later scan preserves the curated BPM/key rather than re-analyzing over it (global standard #7).
/// </summary>
public static class ImportTrackMapper
{
    public static MusicTrack Map(ImportedTrack src, ScannedFile file, MusicTrack? existing, ImportMergePolicy policy)
    {
        BpmResult? importedBpm = src.Bpm is > 0
            ? new BpmResult(src.Bpm.Value, Confidence: 1.0, Math.Max(0.0, src.FirstBeatSeconds ?? 0.0))
            : null;
        MusicalKey? importedKey = ImportKeyParser.Parse(src.Key);
        TimeSpan? importedDuration = src.DurationSeconds is > 0 ? TimeSpan.FromSeconds(src.DurationSeconds.Value) : null;
        TrackMetadata importedMeta = BuildMetadata(src);

        if (existing is null)
        {
            return new MusicTrack(
                File: file,
                Bpm: importedBpm,
                Key: importedKey,
                Duration: importedDuration,
                Cues: TrackCues.None,
                Status: importedBpm is not null ? MediaAnalysisStatus.Ok : MediaAnalysisStatus.PartiallyAnalyzed,
                Error: null,
                Metadata: importedMeta,
                Kind: MusicMediaKind.Track,
                AnalyzerVersion: 0,
                AnalysisIsManual: true);
        }

        bool overwrite = policy == ImportMergePolicy.Overwrite;
        BpmResult? bpm = overwrite ? importedBpm ?? existing.Bpm : existing.Bpm ?? importedBpm;
        MusicalKey? key = overwrite ? importedKey ?? existing.Key : existing.Key ?? importedKey;
        TimeSpan? duration = overwrite ? importedDuration ?? existing.Duration : existing.Duration ?? importedDuration;

        return existing with
        {
            Bpm = bpm,
            Key = key,
            Duration = duration,
            Metadata = MergeMetadata(existing.Metadata, importedMeta, overwrite),
            Status = bpm is not null ? MediaAnalysisStatus.Ok : existing.Status,
            // Protect any analysis we just contributed from being re-analyzed away.
            AnalysisIsManual = existing.AnalysisIsManual || overwrite || (existing.Bpm is null && bpm is not null),
        };
    }

    private static TrackMetadata BuildMetadata(ImportedTrack src) =>
        new(Title: src.Title, Artist: src.Artist, Album: src.Album, AlbumArtist: null,
            Genre: src.Genre, Year: src.Year, TrackNumber: null, Comment: src.Comment,
            BitrateKbps: null, SampleRateHz: null, Channels: null, Codec: null);

    private static TrackMetadata MergeMetadata(TrackMetadata? existing, TrackMetadata imported, bool overwrite)
    {
        if (existing is null)
            return imported;

        string? Pick(string? e, string? i) =>
            overwrite ? (string.IsNullOrWhiteSpace(i) ? e : i) : (string.IsNullOrWhiteSpace(e) ? i : e);
        int? PickInt(int? e, int? i) => overwrite ? i ?? e : e ?? i;

        return existing with
        {
            Title = Pick(existing.Title, imported.Title),
            Artist = Pick(existing.Artist, imported.Artist),
            Album = Pick(existing.Album, imported.Album),
            Genre = Pick(existing.Genre, imported.Genre),
            Year = PickInt(existing.Year, imported.Year),
            Comment = Pick(existing.Comment, imported.Comment),
        };
    }
}
