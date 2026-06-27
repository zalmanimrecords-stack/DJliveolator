using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading.Tasks;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Libraries;

/// <summary>
/// B1 — facet/sort/status filtering on the Libraries tab. Seeds the catalog through the persistence
/// store (so the BPM/key/year/genre are deterministic, not decoder-derived) and drives the filter +
/// sort surface, asserting the visible <see cref="LibrariesViewModel.Tracks"/> narrows and orders.
/// </summary>
public sealed class LibrariesViewModelFilterSortTests : IDisposable
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Each seeded VM starts a background re-analysis pass; track them so Dispose stops every pass before
    // the test ends — a leaked pass mutates UI state on a background thread and races later tests (doc 27 B0).
    private readonly List<IDisposable> _created = new();

    public LibrariesViewModelFilterSortTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    public void Dispose()
    {
        foreach (IDisposable vm in _created)
            vm.Dispose();
    }

    private static MusicTrack Track(
        string path, string artist, string genre, double? bpm, string? camelot, int year,
        int durationSeconds = 240, MediaAnalysisStatus status = MediaAnalysisStatus.Ok)
    {
        var meta = new TrackMetadata(null, artist, null, null, genre, year, null, null, null, null, null, null);
        BpmResult? bpmResult = bpm is { } b ? new BpmResult(b, 0.9) : null;
        MusicalKey? key = camelot is null ? null : new MusicalKey(0, KeyMode.Major, camelot, 0.9);
        return new MusicTrack(
            new ScannedFile(path, 1000, T), bpmResult, key,
            TimeSpan.FromSeconds(durationSeconds), TrackCues.None, status, null, meta);
    }

    private static readonly MusicTrack[] Catalog =
    {
        Track("/music/a.mp3", "M83", "Electronic", 128, "8A", 2011, durationSeconds: 300),
        Track("/music/b.wav", "Deadmau5", "House", 122, "9A", 2008, durationSeconds: 200),
        Track("/music/c.mp3", "M83", "Electronic", 90, "1B", 2011, durationSeconds: 120,
            status: MediaAnalysisStatus.PartiallyAnalyzed),
        Track("/music/d.flac", "Justice", "Electronic", null, null, 2007, durationSeconds: 180,
            status: MediaAnalysisStatus.Failed),
        Track("/music/short.mp3", "One Shot", "Sample", 128, "2A", 2026, durationSeconds: 59),
    };

    private async Task<LibrariesViewModel> SeededViewModelAsync()
    {
        var store = new FakeMusicCatalogStore(seedTracks: Catalog, seedFolders: new[] { "/music" });
        var library = new MusicLibrary(new FakeFileEnumerator(), new FakeAudioDecoder());
        var vm = new LibrariesViewModel(library, store: store);
        _created.Add(vm);
        await vm.InitializeAsync();
        return vm;
    }

    [Fact]
    public async Task Facets_are_populated_from_the_catalog()
    {
        LibrariesViewModel vm = await SeededViewModelAsync();

        Assert.Contains("M83", vm.Artists);
        Assert.Contains("Deadmau5", vm.Artists);
        Assert.Contains("Electronic", vm.Genres);
        Assert.Contains(2011, vm.Years);
        Assert.Contains("mp3", vm.FileTypes);
        // The "(All)" sentinels lead each list so a fresh tab shows everything.
        Assert.Null(vm.SelectedArtist);
        // Short clips (<1 min) are now visible by default — the old hard floor hid them silently (doc 31 H2).
        Assert.Equal(5, vm.Tracks.Count);
        Assert.Contains(vm.Tracks, row => row.Track.File.Path.EndsWith("short.mp3"));
    }

    [Fact]
    public async Task Short_clips_are_shown_by_default_and_counted()
    {
        LibrariesViewModel vm = await SeededViewModelAsync();

        Assert.True(vm.ShowShortClips);
        Assert.Equal(1, vm.ShortClipCount); // short.mp3 is the only sub-minute track
        Assert.Contains(vm.Tracks, row => row.Track.File.Path.EndsWith("short.mp3"));
    }

    [Fact]
    public async Task Hiding_short_clips_excludes_them_but_keeps_the_count_visible()
    {
        LibrariesViewModel vm = await SeededViewModelAsync();

        vm.ShowShortClips = false;

        Assert.Equal(4, vm.Tracks.Count);
        Assert.DoesNotContain(vm.Tracks, row => row.Track.File.Path.EndsWith("short.mp3"));
        Assert.Equal(1, vm.ShortClipCount); // still reported, so the exclusion is never silent
    }

    [Fact]
    public async Task Selecting_an_artist_narrows_the_visible_tracks()
    {
        LibrariesViewModel vm = await SeededViewModelAsync();

        vm.SelectedArtist = "M83";

        Assert.Equal(2, vm.Tracks.Count);
        Assert.All(vm.Tracks, t => Assert.Equal("M83", t.Artist));
    }

    [Fact]
    public async Task Genre_and_status_facets_compose()
    {
        LibrariesViewModel vm = await SeededViewModelAsync();

        vm.SelectedGenre = "Electronic";
        vm.SelectedStatus = MediaAnalysisStatus.Ok;

        // Electronic ∧ Ok excludes the partial (c) and the non-Electronic House track (b).
        TrackRowViewModel row = Assert.Single(vm.Tracks);
        Assert.EndsWith("a.mp3", row.Track.File.Path);
    }

    [Fact]
    public async Task Year_facet_filters()
    {
        LibrariesViewModel vm = await SeededViewModelAsync();

        vm.SelectedYear = 2011;

        Assert.Equal(2, vm.Tracks.Count);
        Assert.All(vm.Tracks, t => Assert.Equal("2011", t.Year));
    }

    [Fact]
    public async Task Search_still_composes_with_facets()
    {
        LibrariesViewModel vm = await SeededViewModelAsync();

        vm.SelectedGenre = "Electronic";
        vm.SearchText = "deadmau"; // Deadmau5 is House, so the combination is empty

        Assert.Empty(vm.Tracks);
    }

    [Fact]
    public async Task Sort_by_bpm_ascending_then_descending()
    {
        LibrariesViewModel vm = await SeededViewModelAsync();

        vm.SortKey = TrackSortKey.Bpm;
        vm.SortDescending = false;
        // short.mp3 (128) is now visible too (doc 31 H2), so 128 appears twice.
        Assert.Equal(new[] { 90.0, 122.0, 128.0, 128.0 }, vm.Tracks.Where(t => t.Track.Bpm is not null).Select(t => t.Track.Bpm!.Bpm));
        // the keyless/BPM-less failed track sorts last
        Assert.Null(vm.Tracks[^1].Track.Bpm);

        vm.SortDescending = true;
        Assert.Equal(new[] { 128.0, 128.0, 122.0, 90.0 }, vm.Tracks.Where(t => t.Track.Bpm is not null).Select(t => t.Track.Bpm!.Bpm));
        Assert.Null(vm.Tracks[^1].Track.Bpm); // still last, even descending
    }

    [Fact]
    public async Task Sort_by_duration_ascending()
    {
        LibrariesViewModel vm = await SeededViewModelAsync();

        vm.SortKey = TrackSortKey.Duration;
        vm.SortDescending = false;

        Assert.Equal(
            new[] { "/music/short.mp3", "/music/c.mp3", "/music/d.flac", "/music/b.wav", "/music/a.mp3" },
            vm.Tracks.Select(t => t.Track.File.Path));
    }

    [Fact]
    public async Task ClearFilters_resets_facets_search_and_shows_all()
    {
        LibrariesViewModel vm = await SeededViewModelAsync();
        vm.SelectedArtist = "M83";
        vm.SelectedStatus = MediaAnalysisStatus.Ok;
        vm.SearchText = "zzz";

        vm.ClearFiltersCommand.Execute().Subscribe();

        Assert.Null(vm.SelectedArtist);
        Assert.Null(vm.SelectedStatus);
        Assert.True(string.IsNullOrEmpty(vm.SearchText));
        Assert.Equal(5, vm.Tracks.Count); // short clips visible by default (doc 31 H2)
    }
}
