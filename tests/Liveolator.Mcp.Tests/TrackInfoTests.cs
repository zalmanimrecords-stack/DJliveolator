using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Mcp.Contracts;

namespace Liveolator.Mcp.Tests;

public sealed class TrackInfoTests
{
    [Fact]
    public void From_IncludesMetadataAndAnalysisProvenance()
    {
        var metadata = TrackMetadata.Empty with
        {
            Title = "Tagged title",
            Artist = "Artist",
            Album = "Album",
            Genre = "Techno",
            Year = 2026,
            BitrateKbps = 320,
            Codec = "MP3",
        };
        var track = new MusicTrack(
            new ScannedFile("C:/music/track.mp3", 42, DateTime.UtcNow),
            new BpmResult(128, 0.9),
            new MusicalKey(0, KeyMode.Major, "8B", 0.8),
            TimeSpan.FromMinutes(5),
            TrackCues.None,
            MediaAnalysisStatus.Ok,
            null,
            metadata,
            MusicMediaKind.Sample,
            AnalyzerVersion: 7,
            AnalysisIsManual: true);

        TrackInfo result = TrackInfo.From(track);

        Assert.Equal("Artist", result.Artist);
        Assert.Equal("Techno", result.Genre);
        Assert.Equal("mp3", result.FileType);
        Assert.Equal("Sample", result.Kind);
        Assert.Equal(7, result.AnalyzerVersion);
        Assert.True(result.AnalysisIsManual);
    }
}
