using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;

namespace Liveolator.Media.Tests;

/// <summary>Builds in-memory <see cref="MusicTrack"/> values for persistence/export tests.</summary>
internal static class TestTracks
{
    private static readonly DateTime T = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public static MusicTrack Analyzed(
        string path, double bpm, int tonic, KeyMode mode,
        TrackMetadata? metadata = null, MusicMediaKind kind = MusicMediaKind.Track)
    {
        var key = new MusicalKey(tonic, mode, Camelot.Code(tonic, mode), 0.85);
        var cues = new TrackCues(TimeSpan.FromSeconds(2), null, null, TimeSpan.FromMinutes(3.5));
        return new MusicTrack(
            new ScannedFile(path, 4096, T),
            new BpmResult(bpm, 0.9),
            key,
            TimeSpan.FromMinutes(4),
            cues,
            MediaAnalysisStatus.Ok,
            null,
            metadata,
            kind,
            TrackAnalyzer.CurrentVersion);
    }

    public static MusicTrack Failed(string path)
        => new(new ScannedFile(path, 0, T), null, null, null, TrackCues.None, MediaAnalysisStatus.Failed, "decode error");

    public static VisualAsset Video(string path, int width, int height, double seconds)
        => new(
            new ScannedFile(path, 8192, T),
            VisualMediaKind.Video,
            new VisualMediaInfo(width, height, TimeSpan.FromSeconds(seconds)),
            MediaAnalysisStatus.Ok,
            null);

    public static VisualAsset Image(string path, int width, int height)
        => new(
            new ScannedFile(path, 2048, T),
            VisualMediaKind.Image,
            new VisualMediaInfo(width, height, Duration: null),
            MediaAnalysisStatus.Ok,
            null);
}
