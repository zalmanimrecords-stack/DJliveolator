using Liveolator.Core.Analysis;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Xunit;

namespace Liveolator.Core.Tests.Library;

public class TrackFacetsTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static MusicTrack Track(string path, string? artist, string? genre, int? year)
    {
        var meta = new TrackMetadata(null, artist, null, null, genre, year, null, null, null, null, null, null);
        return new MusicTrack(new ScannedFile(path, 1000, T), null, null, null, TrackCues.None,
            MediaAnalysisStatus.Ok, null, meta);
    }

    [Fact]
    public void Of_returns_distinct_sorted_facets_without_blanks()
    {
        var tracks = new[]
        {
            Track("/m/a.mp3", "M83", "Electronic", 2011),
            Track("/m/b.wav", "Deadmau5", "House", 2008),
            Track("/m/c.flac", "M83", null, 2011),      // duplicate artist; null genre
            Track("/m/d.mp3", "  ", "Electronic", null), // blank artist dropped
        };

        TrackFacets facets = TrackFacets.Of(tracks);

        Assert.Equal(new[] { "Deadmau5", "M83" }, facets.Artists);          // distinct + sorted, blank dropped
        Assert.Equal(new[] { "Electronic", "House" }, facets.Genres);       // null dropped
        Assert.Equal(new[] { 2011, 2008 }, facets.Years);                   // distinct, newest first
        Assert.Equal(new[] { "flac", "mp3", "wav" }, facets.FileTypes);     // by extension, sorted
    }

    [Fact]
    public void Of_empty_catalog_is_empty()
    {
        TrackFacets facets = TrackFacets.Of(Array.Empty<MusicTrack>());
        Assert.Empty(facets.Artists);
        Assert.Empty(facets.Genres);
        Assert.Empty(facets.Years);
        Assert.Empty(facets.FileTypes);
    }
}
