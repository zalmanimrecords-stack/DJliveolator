using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Enrichment;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Tests.Library;
using Xunit;

namespace Liveolator.Core.Tests.Enrichment;

/// <summary>
/// The online genre-enrichment pass (<see cref="CatalogEnrichmentService"/>): it must look up only the
/// tracks missing a genre, skip those that already have one, survive a lookup that throws or returns
/// null, honour cancellation, and apply any returned genre to the catalog — all with a FAKE provider and
/// no network (global standards #16/#26).
/// </summary>
public class CatalogEnrichmentServiceTests
{
    private static readonly DateTime T = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static MusicTrack Track(string path, string? genre, string? artist = "Some Artist", string? title = "Some Title")
    {
        var key = new MusicalKey(0, KeyMode.Major, Camelot.Code(0, KeyMode.Major), 0.9);
        var metadata = new TrackMetadata(title, artist, null, null, genre, null, null, null, null, null, null, null);
        return new MusicTrack(
            new ScannedFile(path, 10, T), new BpmResult(128.0, 0.9), key,
            TimeSpan.FromMinutes(4), TrackCues.None, MediaAnalysisStatus.Ok, null, metadata,
            AnalyzerVersion: TrackAnalyzer.CurrentVersion);
    }

    private static MusicLibrary LibraryWith(params MusicTrack[] tracks)
    {
        var library = new MusicLibrary(new FakeFileEnumerator(), new MapAudioDecoder(new()));
        library.Restore(tracks);
        return library;
    }

    private static CatalogEnrichmentService Service(MusicLibrary library, IMetadataProvider provider)
        => new(library, provider, store: null, delay: TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_FillsMissingGenre_AndSkipsTracksThatAlreadyHaveOne()
    {
        MusicLibrary library = LibraryWith(
            Track("a.wav", genre: null, title: "A"),
            Track("b.wav", genre: "House", title: "B"));
        var provider = new FakeMetadataProvider(_ => new OnlineTrackMetadata(null, null, null, "Techno", "Fake"));

        EnrichmentOutcome outcome = await Service(library, provider).RunAsync();

        Assert.Equal(1, outcome.Considered); // only the genre-less track
        Assert.Equal(1, outcome.Enriched);
        Assert.Equal("Techno", library.TryGet("a.wav")!.Metadata!.Genre);
        Assert.Equal("House", library.TryGet("b.wav")!.Metadata!.Genre); // untouched
        Assert.DoesNotContain("B", provider.LookedUp); // never queried an already-tagged track
    }

    [Fact]
    public async Task RunAsync_ContinuesPastALookupThatThrowsOrReturnsNull()
    {
        MusicLibrary library = LibraryWith(
            Track("throw.wav", genre: null, title: "Throw"),
            Track("null.wav", genre: null, title: "Null"),
            Track("ok.wav", genre: null, title: "Ok"));
        var provider = new FakeMetadataProvider(q => q.Title switch
        {
            "Throw" => throw new InvalidOperationException("boom"),
            "Null" => null,
            _ => new OnlineTrackMetadata(null, null, null, "Trance", "Fake"),
        });
        var errors = new List<string>();

        var service = new CatalogEnrichmentService(library, provider, delay: TimeSpan.Zero, onError: errors.Add);
        EnrichmentOutcome outcome = await service.RunAsync();

        Assert.Equal(3, outcome.Considered);
        Assert.Equal(1, outcome.Enriched);             // only ok.wav
        Assert.Equal("Trance", library.TryGet("ok.wav")!.Metadata!.Genre);
        Assert.Null(library.TryGet("null.wav")!.Metadata!.Genre);
        Assert.Single(errors);                         // the throw was recorded, not propagated
    }

    [Fact]
    public async Task RunAsync_DerivesArtistTitleFromFilenameWhenTagsMissing()
    {
        // No artist/title tags → fall back to the "Artist - Title" filename convention.
        MusicLibrary library = LibraryWith(Track("Daft Punk - Da Funk.mp3", genre: null, artist: null, title: null));
        var provider = new FakeMetadataProvider(_ => new OnlineTrackMetadata(null, null, null, "French House", "Fake"));

        await Service(library, provider).RunAsync();

        TrackLookupQuery query = provider.Queries.Single();
        Assert.Equal("Daft Punk", query.Artist);
        Assert.Equal("Da Funk", query.Title);
        Assert.Equal("French House", library.TryGet("Daft Punk - Da Funk.mp3")!.Metadata!.Genre);
    }

    [Fact]
    public async Task RunAsync_HonoursCancellation()
    {
        MusicLibrary library = LibraryWith(Track("a.wav", genre: null), Track("b.wav", genre: null));
        using var cts = new CancellationTokenSource();
        var provider = new FakeMetadataProvider(_ =>
        {
            cts.Cancel(); // cancel during the first lookup
            return new OnlineTrackMetadata(null, null, null, "Techno", "Fake");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service(library, provider).RunAsync(cancellationToken: cts.Token));
    }

    [Fact]
    public async Task RunAsync_WhenNothingMissingGenre_DoesNoWork()
    {
        MusicLibrary library = LibraryWith(Track("a.wav", genre: "House"));
        var provider = new FakeMetadataProvider(_ => new OnlineTrackMetadata(null, null, null, "Techno", "Fake"));

        EnrichmentOutcome outcome = await Service(library, provider).RunAsync();

        Assert.Equal(0, outcome.Considered);
        Assert.Empty(provider.LookedUp);
    }

    private sealed class FakeMetadataProvider : IMetadataProvider
    {
        private readonly Func<TrackLookupQuery, OnlineTrackMetadata?> _respond;
        public List<TrackLookupQuery> Queries { get; } = new();
        public List<string?> LookedUp => Queries.Select(q => q.Title).ToList();

        public FakeMetadataProvider(Func<TrackLookupQuery, OnlineTrackMetadata?> respond) => _respond = respond;

        public Task<OnlineTrackMetadata?> LookupAsync(TrackLookupQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(_respond(query));
        }
    }
}
