using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Xunit;

namespace Liveolator.Core.Tests.Library;

public class TrackFilterTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static MusicTrack Track(
        string path, string? artist = null, string? genre = null, double bpm = 120,
        string camelot = "8A", int? year = null, MusicMediaKind kind = MusicMediaKind.Track)
    {
        var meta = new TrackMetadata(null, artist, null, null, genre, year, null, null, null, null, null, null);
        return new MusicTrack(
            new ScannedFile(path, 1000, T),
            new BpmResult(bpm, 0.9),
            new MusicalKey(0, KeyMode.Major, camelot, 0.9),
            TimeSpan.FromMinutes(4), TrackCues.None, MediaAnalysisStatus.Ok, null, meta, kind);
    }

    private static readonly MusicTrack[] Catalog =
    {
        Track("/m/a.mp3", artist: "M83", genre: "Electronic", bpm: 128, camelot: "8A", year: 2011, kind: MusicMediaKind.Track),
        Track("/m/b.wav", artist: "Deadmau5", genre: "House", bpm: 122, camelot: "9A", year: 2008, kind: MusicMediaKind.Track),
        Track("/loops/c.wav", artist: "M83", genre: "Electronic", bpm: 90, camelot: "8A", year: 2011, kind: MusicMediaKind.Sample),
    };

    [Fact]
    public void Kind_filters_tracks_vs_samples()
    {
        var samples = TrackQuery.Apply(Catalog, new TrackFilter(Kind: MusicMediaKind.Sample));
        Assert.Equal("/loops/c.wav", Assert.Single(samples).File.Path);
    }

    [Fact]
    public void Artist_filter_is_exact_case_insensitive()
    {
        var byArtist = TrackQuery.Apply(Catalog, new TrackFilter(Artist: "m83"));
        Assert.Equal(2, byArtist.Count);
        Assert.All(byArtist, t => Assert.Equal("M83", t.Artist));
    }

    [Fact]
    public void Genre_and_BpmRange_combine()
    {
        var result = TrackQuery.Apply(Catalog, new TrackFilter(Genre: "Electronic", MinBpm: 100));
        Assert.Equal("/m/a.mp3", Assert.Single(result).File.Path); // the 90-BPM electronic sample is excluded
    }

    [Fact]
    public void Year_and_FileType_filter()
    {
        Assert.Equal(2, TrackQuery.Apply(Catalog, new TrackFilter(Year: 2011)).Count);
        Assert.Equal("/m/b.wav", Assert.Single(TrackQuery.Apply(Catalog, new TrackFilter(FileType: "wav", Kind: MusicMediaKind.Track))).File.Path);
    }

    [Fact]
    public void Camelot_filter_exact()
        => Assert.Equal(2, TrackQuery.Apply(Catalog, new TrackFilter(Camelot: "8A")).Count);

    [Fact]
    public void Empty_filter_returns_all_ordered()
    {
        var all = TrackQuery.Apply(Catalog, new TrackFilter());
        Assert.Equal(3, all.Count);
    }
}
