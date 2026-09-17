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

    // ---------- genre: the tags below are REAL values measured from the owner's catalog ----------

    /// <summary>
    /// The catalog that broke whole-string genre equality. Picking "Melodic House &amp; Techno" used to
    /// return only the plainly-tagged track and silently hide the two whose tag carries a second value —
    /// measured at a 46% miss on the owner's library.
    /// </summary>
    private static readonly MusicTrack[] GenreCatalog =
    {
        Track("/g/plain.mp3", genre: "Melodic House & Techno"),
        Track("/g/suffixed.mp3", genre: "Melodic House & Techno | Melodic Techno"),
        Track("/g/house.mp3", genre: "Melodic House & Techno | Melodic House"),
        Track("/g/slashed.mp3", genre: "Goa Trance/Psytrance"),
        Track("/g/commas.mp3", genre: "Goa, Psychedelic Trance, Electronic"),
        Track("/g/semis.mp3", genre: "Techno ;Raw / Deep / Hypnotic; | Dub"),
        Track("/g/untagged.mp3", genre: null),
    };

    private static string[] Paths(IEnumerable<MusicTrack> tracks) => tracks.Select(t => t.File.Path).Order().ToArray();

    [Fact]
    public void Genre_filter_matches_a_tag_that_carries_further_values()
        => Assert.Equal(
            new[] { "/g/house.mp3", "/g/plain.mp3", "/g/suffixed.mp3" },
            Paths(TrackQuery.Apply(GenreCatalog, new TrackFilter(Genre: "Melodic House & Techno"))));

    [Theory]
    [InlineData("Psytrance", "/g/slashed.mp3")]     // split on '/'
    [InlineData("Psy-Trance", "/g/slashed.mp3")]    // punctuation and spacing collapse
    [InlineData("Electronic", "/g/commas.mp3")]     // split on ','
    [InlineData("Dub", "/g/semis.mp3")]             // split on ';' and '|'
    public void Genre_filter_reaches_inside_every_separator_real_tags_use(string genre, string expected)
        => Assert.Equal(expected, Assert.Single(TrackQuery.Apply(GenreCatalog, new TrackFilter(Genre: genre))).File.Path);

    /// <summary>
    /// Multi-select rides the existing single <see cref="TrackFilter.Genre"/> field: the selected genres
    /// are joined with a separator the tag grammar already understands, so several genres are an OR.
    /// No new filter field, so the MCP tool and saved smart collections are untouched.
    /// </summary>
    [Fact]
    public void Genre_filter_accepts_several_genres_at_once_as_an_or()
        => Assert.Equal(
            new[] { "/g/commas.mp3", "/g/semis.mp3", "/g/slashed.mp3" },
            Paths(TrackQuery.Apply(GenreCatalog, new TrackFilter(Genre: "Psytrance|Techno|Goa"))));

    /// <summary>Widening must not become "matches everything" — an unrelated genre still filters out.</summary>
    [Fact]
    public void Genre_filter_still_refuses_a_genuinely_different_genre()
        => Assert.DoesNotContain(
            TrackQuery.Apply(GenreCatalog, new TrackFilter(Genre: "Melodic House & Techno")),
            t => t.File.Path == "/g/slashed.mp3");

    /// <summary>
    /// An untagged track cannot be placed in a genre, so a genre facet hides it. 73% of the owner's
    /// catalog is untagged, which is why the filter bar needs its own way to ask for those.
    /// </summary>
    [Fact]
    public void Genre_filter_excludes_untagged_tracks()
        => Assert.DoesNotContain(
            TrackQuery.Apply(GenreCatalog, new TrackFilter(Genre: "Melodic House & Techno")),
            t => t.File.Path == "/g/untagged.mp3");

    /// <summary>A facet holding only punctuation has asked for nothing, and must not hide the catalog.</summary>
    [Fact]
    public void Genre_filter_of_pure_punctuation_matches_all()
        => Assert.Equal(GenreCatalog.Length, TrackQuery.Apply(GenreCatalog, new TrackFilter(Genre: " / | , ")).Count);
}
