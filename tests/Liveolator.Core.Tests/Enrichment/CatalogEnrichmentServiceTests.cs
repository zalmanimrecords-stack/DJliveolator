using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Enrichment;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Tests.Library;
using Xunit;

namespace Liveolator.Core.Tests.Enrichment;

/// <summary>
/// The online enrichment pass (<see cref="CatalogEnrichmentService"/>): it must look up every track not
/// yet checked online (genre fill + BPM cross-check), stamp each completed lookup so the free API is
/// never re-queried for the same track, apply BPM-only results, distrust filename-guessed identities
/// for the BPM verdict, persist per track, survive a lookup that throws or returns null, and honour
/// cancellation — all with a FAKE provider and no network (global standards #16/#26).
/// </summary>
public class CatalogEnrichmentServiceTests
{
    private static readonly DateTime T = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static MusicTrack Track(
        string path, string? genre, string? artist = "Some Artist", string? title = "Some Title",
        DateTime? checkedUtc = null, double bpm = 128.0)
    {
        var key = new MusicalKey(0, KeyMode.Major, Camelot.Code(0, KeyMode.Major), 0.9);
        var metadata = new TrackMetadata(title, artist, null, null, genre, null, null, null, null, null, null, null);
        return new MusicTrack(
            new ScannedFile(path, 10, T), new BpmResult(bpm, 0.9), key,
            TimeSpan.FromMinutes(4), TrackCues.None, MediaAnalysisStatus.Ok, null, metadata,
            AnalyzerVersion: TrackAnalyzer.CurrentVersion, OnlineLookupUtc: checkedUtc);
    }

    private static MusicLibrary LibraryWith(params MusicTrack[] tracks)
    {
        var library = new MusicLibrary(new FakeFileEnumerator(), new MapAudioDecoder(new()));
        library.Restore(tracks);
        return library;
    }

    private static CatalogEnrichmentService Service(
        MusicLibrary library, IMetadataProvider provider, IMusicCatalogStore? store = null)
        => new(library, provider, store, delay: TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_FillsMissingGenre_AndSkipsTracksAlreadyCheckedOnline()
    {
        MusicLibrary library = LibraryWith(
            Track("a.wav", genre: null, title: "A"),
            Track("b.wav", genre: "House", title: "B", checkedUtc: T)); // already checked online
        var provider = new FakeMetadataProvider(_ => new OnlineTrackMetadata(null, null, null, "Techno", "Fake"));

        EnrichmentOutcome outcome = await Service(library, provider).RunAsync();

        Assert.Equal(1, outcome.Considered); // only the never-checked track
        Assert.Equal(1, outcome.Enriched);
        Assert.Equal("Techno", library.TryGet("a.wav")!.Metadata!.Genre);
        Assert.Equal("House", library.TryGet("b.wav")!.Metadata!.Genre); // untouched
        Assert.DoesNotContain("B", provider.LookedUp); // never re-queried an already-checked track
    }

    [Fact]
    public async Task RunAsync_CrossChecksBpm_OnATrackThatAlreadyHasAGenre()
    {
        // A fully tagged track still gets ONE online pass — that's the BPM cross-check.
        MusicLibrary library = LibraryWith(Track("a.wav", genre: "Drum & Bass", title: "A", bpm: 128.0));
        var provider = new FakeMetadataProvider(_ => new OnlineTrackMetadata(174, null, null, null, "Fake"));

        EnrichmentOutcome outcome = await Service(library, provider).RunAsync();

        MusicTrack track = library.TryGet("a.wav")!;
        Assert.Equal(1, outcome.Enriched);
        Assert.Equal(128.0, track.Bpm!.Bpm);                          // local stays authoritative
        Assert.Equal(174, track.OnlineBpm);
        Assert.Equal(BpmProvenance.Conflicted, track.BpmProvenance);  // the visible flag
        Assert.NotNull(track.OnlineLookupUtc);
    }

    [Fact]
    public async Task RunAsync_StampsCompletedLookups_SoASecondRunSkipsThem()
    {
        // A lookup that finds NOTHING must still be stamped, or every scan re-burns the free API.
        MusicLibrary library = LibraryWith(Track("a.wav", genre: null, title: "A"));
        var provider = new FakeMetadataProvider(_ => null);

        await Service(library, provider).RunAsync();
        Assert.NotNull(library.TryGet("a.wav")!.OnlineLookupUtc);

        EnrichmentOutcome second = await Service(library, provider).RunAsync();
        Assert.Equal(0, second.Considered);
        Assert.Single(provider.LookedUp); // one lookup total, not one per run
    }

    [Fact]
    public async Task RunAsync_FilenameGuessedIdentity_AppliesGenreButNeverABpmVerdict()
    {
        // "Artist - Title" parsed from the filename is too weak an identity for a BPM verdict — an
        // extended mix matched to the radio edit paints a false conflict. Genre is still worth taking.
        MusicLibrary library = LibraryWith(
            Track("Daft Punk - Da Funk.mp3", genre: null, artist: null, title: null, bpm: 111.0));
        var provider = new FakeMetadataProvider(_ => new OnlineTrackMetadata(174, null, null, "French House", "Fake"));

        await Service(library, provider).RunAsync();

        MusicTrack track = library.TryGet("Daft Punk - Da Funk.mp3")!;
        Assert.Equal("French House", track.Metadata!.Genre);
        Assert.Null(track.OnlineBpm);
        Assert.NotEqual(BpmProvenance.Conflicted, track.BpmProvenance);
    }

    [Fact]
    public async Task RunAsync_PersistsEachProcessedTrack_NeverTheWholeCatalog()
    {
        // A 5,000-track pass must not rewrite the whole catalog every N tracks (doc 31 M1: per-row saves).
        MusicLibrary library = LibraryWith(
            Track("a.wav", genre: null, title: "A"),
            Track("b.wav", genre: null, title: "B"));
        var provider = new FakeMetadataProvider(_ => new OnlineTrackMetadata(null, null, null, "Techno", "Fake"));
        var store = new CountingCatalogStore();

        await Service(library, provider, store).RunAsync();

        Assert.Equal(new[] { "a.wav", "b.wav" }, store.SavedTracks);
        Assert.Equal(0, store.WholeCatalogSaves);
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
        // A transport failure is retryable next run; a completed miss is not.
        Assert.Null(library.TryGet("throw.wav")!.OnlineLookupUtc);
        Assert.NotNull(library.TryGet("null.wav")!.OnlineLookupUtc);
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
    public async Task RunAsync_WhenEveryTrackWasAlreadyChecked_DoesNoWork()
    {
        MusicLibrary library = LibraryWith(Track("a.wav", genre: "House", checkedUtc: T));
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

    /// <summary>Counts per-track vs whole-catalog saves; every other seam member is inert.</summary>
    private sealed class CountingCatalogStore : IMusicCatalogStore
    {
        public List<string> SavedTracks { get; } = new();
        public int WholeCatalogSaves { get; private set; }

        public Task SaveTrackAsync(MusicTrack track, CancellationToken cancellationToken = default)
        {
            SavedTracks.Add(track.File.Path);
            return Task.CompletedTask;
        }

        public Task SaveMusicAsync(IEnumerable<MusicTrack> tracks, CancellationToken cancellationToken = default)
        {
            WholeCatalogSaves++;
            return Task.CompletedTask;
        }

        public Task DeleteTrackAsync(string path, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<MusicTrack>> LoadMusicAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MusicTrack>>(Array.Empty<MusicTrack>());

        public Task<IReadOnlyList<string>> LoadScanFoldersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task SaveScanFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<string>> LoadSampleFoldersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task SaveSampleFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
