using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Xunit;

namespace Liveolator.Core.Tests.Library;

public class TrackQueryTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static MusicTrack Track(
        string path,
        double? bpm = null,
        string? camelot = null,
        string? title = null,
        string? artist = null,
        TimeSpan? duration = null)
    {
        TrackMetadata? metadata = title is null && artist is null
            ? null
            : TrackMetadata.Empty with { Title = title, Artist = artist };

        return new MusicTrack(
            new ScannedFile(path, 1000, T),
            bpm is null ? null : new BpmResult(bpm.Value, 0.9),
            camelot is null ? null : new MusicalKey(0, KeyMode.Major, camelot, 0.9),
            duration ?? TimeSpan.FromMinutes(4),
            TrackCues.None,
            MediaAnalysisStatus.Ok,
            null,
            metadata);
    }

    [Fact]
    public void Search_NoFilters_ReturnsAllOrderedByTitle()
    {
        var tracks = new[] { Track("Zebra.mp3"), Track("Apple.mp3"), Track("Mango.mp3") };

        IReadOnlyList<MusicTrack> result = TrackQuery.Search(tracks);

        Assert.Equal(new[] { "Apple", "Mango", "Zebra" }, result.Select(t => t.Title));
    }

    [Fact]
    public void Search_Text_MatchesTitleSubstring_CaseInsensitive()
    {
        var tracks = new[] { Track("a.mp3", title: "Midnight Strobe"), Track("b.mp3", title: "Sunrise") };

        IReadOnlyList<MusicTrack> result = TrackQuery.Search(tracks, text: "strobe");

        Assert.Equal("Midnight Strobe", Assert.Single(result).Title);
    }

    [Fact]
    public void Search_Text_MatchesArtist()
    {
        var tracks = new[] { Track("a.mp3", title: "Track A", artist: "Boris Brejcha"), Track("b.mp3", title: "Track B") };

        IReadOnlyList<MusicTrack> result = TrackQuery.Search(tracks, text: "brejcha");

        Assert.Equal("Track A", Assert.Single(result).Title);
    }

    [Fact]
    public void Search_Text_MatchesFileName_EvenWhenTitleTagDiffers()
    {
        // Tag title is "Sunrise" but the file is track01.mp3 — searching the file name still finds it.
        var tracks = new[] { Track("C:/music/track01.mp3", title: "Sunrise"), Track("C:/music/other.mp3", title: "Nope") };

        IReadOnlyList<MusicTrack> result = TrackQuery.Search(tracks, text: "track01");

        Assert.Equal("Sunrise", Assert.Single(result).Title);
    }

    [Fact]
    public void Search_Text_NoMatch_ReturnsEmpty()
        => Assert.Empty(TrackQuery.Search(new[] { Track("a.mp3", title: "Alpha") }, text: "zzz"));

    [Fact]
    public void Search_MultiTerm_MatchesAcrossGenreAndBpm()
    {
        MusicTrack house124 = WithGenre("a.mp3", "Groove", "House", 124.0);
        MusicTrack techno128 = WithGenre("b.mp3", "Pulse", "Techno", 128.0);

        // "house 124" = two terms; each must match some field (genre "House" AND ~124 BPM).
        IReadOnlyList<MusicTrack> result = TrackQuery.Search(new[] { house124, techno128 }, text: "house 124");

        Assert.Equal("Groove", Assert.Single(result).Title);
    }

    private static MusicTrack WithGenre(string path, string title, string genre, double bpm)
        => new(new ScannedFile(path, 1000, T), new BpmResult(bpm, 0.9), null,
               TimeSpan.FromMinutes(4), TrackCues.None, MediaAnalysisStatus.Ok, null,
               TrackMetadata.Empty with { Title = title, Genre = genre });

    [Fact]
    public void Search_BpmRange_ExcludesOutOfRangeAndUnknownTempo()
    {
        var tracks = new[]
        {
            Track("slow.mp3", bpm: 120, title: "Slow"),
            Track("fast.mp3", bpm: 128, title: "Fast"),
            Track("none.mp3", title: "NoTempo"),
        };

        IReadOnlyList<MusicTrack> result = TrackQuery.Search(tracks, minBpm: 125, maxBpm: 130);

        Assert.Equal("Fast", Assert.Single(result).Title);
    }

    [Fact]
    public void Search_Camelot_IsExactCaseInsensitive()
    {
        var tracks = new[] { Track("a.mp3", camelot: "8B", title: "A"), Track("b.mp3", camelot: "9A", title: "B") };

        IReadOnlyList<MusicTrack> result = TrackQuery.Search(tracks, camelot: "8b");

        Assert.Equal("A", Assert.Single(result).Title);
    }

    [Fact]
    public void Search_CombinesFilters()
    {
        var tracks = new[]
        {
            Track("a.mp3", bpm: 126, camelot: "8B", title: "Deep Strobe"),
            Track("b.mp3", bpm: 126, camelot: "8B", title: "Calm Pad"),     // wrong text
            Track("c.mp3", bpm: 100, camelot: "8B", title: "Slow Strobe"),  // wrong bpm
        };

        IReadOnlyList<MusicTrack> result = TrackQuery.Search(tracks, text: "strobe", minBpm: 120, camelot: "8B");

        Assert.Equal("Deep Strobe", Assert.Single(result).Title);
    }

    [Fact]
    public void Apply_MinDuration_HidesShortTracksButKeepsBoundaryAndUnknownDuration()
    {
        MusicTrack unknown = Track("unknown.mp3") with { Duration = null };
        var tracks = new[]
        {
            Track("short.mp3", duration: TimeSpan.FromSeconds(59)),
            Track("boundary.mp3", duration: TimeSpan.FromMinutes(1)),
            Track("long.mp3", duration: TimeSpan.FromMinutes(4)),
            unknown,
        };

        IReadOnlyList<MusicTrack> result = TrackQuery.Apply(
            tracks,
            new TrackFilter(MinDuration: TimeSpan.FromMinutes(1)));

        Assert.Equal(
            new[] { "boundary.mp3", "long.mp3", "unknown.mp3" },
            result.Select(t => Path.GetFileName(t.File.Path)));
    }

    [Fact]
    public void Search_ClampsLimit()
    {
        var tracks = new[] { Track("a.mp3", title: "A"), Track("b.mp3", title: "B"), Track("c.mp3", title: "C") };

        Assert.Single(TrackQuery.Search(tracks, limit: 1));
        Assert.Single(TrackQuery.Search(tracks, limit: 0)); // clamped up to 1
    }

    [Fact]
    public void Query_FiltersThenSortsAndPages()
    {
        var tracks = new[]
        {
            Track("a.mp3", bpm: 120, title: "A"),
            Track("b.mp3", bpm: 130, title: "B"),
            Track("c.mp3", bpm: 125, title: "C"),
        };

        IReadOnlyList<MusicTrack> result = TrackQuery.Query(
            tracks,
            new TrackFilter(MinBpm: 120),
            TrackSortKey.Bpm,
            descending: true,
            limit: 1,
            offset: 1);

        Assert.Equal("C", Assert.Single(result).Title);
    }

    [Fact]
    public void Search_NullTracks_Throws()
        => Assert.Throws<ArgumentNullException>(() => TrackQuery.Search(null!));
}
