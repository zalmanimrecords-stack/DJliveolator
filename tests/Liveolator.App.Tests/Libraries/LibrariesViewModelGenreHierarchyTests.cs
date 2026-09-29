using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading.Tasks;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Libraries;

/// <summary>
/// Phase 2 (genre-hierarchy wiring) — the Libraries genre picker widens to a two-level (parent/child)
/// facet when a <see cref="GenreHierarchy"/> is loaded, and <see cref="LibrariesViewModel.ApplyFilter"/>
/// threads it through <see cref="TrackQuery"/>. Phase 0/1 (the Core <see cref="GenreHierarchy"/> and its
/// store) are covered elsewhere; these tests only prove the App-layer wiring.
/// </summary>
public sealed class LibrariesViewModelGenreHierarchyTests : IDisposable
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly List<IDisposable> _created = new();

    public LibrariesViewModelGenreHierarchyTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    public void Dispose()
    {
        foreach (IDisposable vm in _created)
            vm.Dispose();
    }

    private static MusicTrack Track(string path, string genre)
    {
        var meta = new TrackMetadata(null, "Some Artist", null, null, genre, 2024, null, null, null, null, null, null);
        return new MusicTrack(
            new ScannedFile(path, 1000, T), new BpmResult(128, 0.9), null,
            TimeSpan.FromSeconds(240), TrackCues.None, MediaAnalysisStatus.Ok, null, meta);
    }

    // House/Deep House/Techno: "Deep House" is a child of "House" in the curated tests; "Techno" always
    // stays top-level with no children, exercising the no-children backward-compat path.
    private static readonly MusicTrack[] Catalog =
    {
        Track("/music/house.mp3", "House"),
        Track("/music/deep.mp3", "Deep House"),
        Track("/music/techno.mp3", "Techno"),
    };

    private async Task<LibrariesViewModel> SeededViewModelAsync(IGenreHierarchyStore? genreHierarchyStore = null)
    {
        var store = new FakeMusicCatalogStore(seedTracks: Catalog, seedFolders: new[] { "/music" });
        var library = new MusicLibrary(new FakeFileEnumerator(), new FakeAudioDecoder());
        var vm = new LibrariesViewModel(library, store: store, genreHierarchyStore: genreHierarchyStore);
        _created.Add(vm);
        await vm.InitializeAsync();
        return vm;
    }

    [Fact]
    public async Task No_store_leaves_every_genre_flat_and_top_level_stop_ship_regression_guard()
    {
        LibrariesViewModel vm = await SeededViewModelAsync(genreHierarchyStore: null);

        Assert.Equal(
            new[] { "Deep House", "House", "Techno" },
            vm.Genres.Select(g => g.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(vm.ChildGenres);

        // Toggling every top-level option must never populate a child list with no hierarchy loaded.
        foreach (GenreFilterOption option in vm.Genres.ToList())
            option.IsSelected = true;

        Assert.Empty(vm.ChildGenres);
        Assert.Null(vm.ExpandedParentGenre);
    }

    [Fact]
    public async Task Empty_hierarchy_from_a_store_is_byte_for_byte_identical_to_no_store()
    {
        LibrariesViewModel withStore = await SeededViewModelAsync(new FakeGenreHierarchyStore());
        LibrariesViewModel withoutStore = await SeededViewModelAsync(genreHierarchyStore: null);

        Assert.Equal(
            withoutStore.Genres.Select(g => g.Name),
            withStore.Genres.Select(g => g.Name));
        Assert.Empty(withStore.ChildGenres);
    }

    private static GenreHierarchy HouseWithDeepHouseChild()
        => new GenreHierarchy().SetParent("Deep House", "House");

    [Fact]
    public async Task Child_genre_is_hidden_from_top_level_and_revealed_by_expanding_its_parent()
    {
        LibrariesViewModel vm = await SeededViewModelAsync(new FakeGenreHierarchyStore(HouseWithDeepHouseChild()));

        Assert.Contains(vm.Genres, g => g.Name == "House");
        Assert.DoesNotContain(vm.Genres, g => g.Name == "Deep House");
        Assert.Empty(vm.ChildGenres); // nothing expanded yet

        vm.Genres.Single(g => g.Name == "House").IsSelected = true;

        Assert.Equal("House", vm.ExpandedParentGenre);
        Assert.Equal(new[] { "Deep House" }, vm.ChildGenres.Select(g => g.Name));
    }

    [Fact]
    public async Task Selecting_the_parent_still_matches_a_track_tagged_only_with_the_child_genre()
    {
        LibrariesViewModel vm = await SeededViewModelAsync(new FakeGenreHierarchyStore(HouseWithDeepHouseChild()));

        vm.Genres.Single(g => g.Name == "House").IsSelected = true;

        Assert.Contains(vm.Tracks, t => t.Track.File.Path == "/music/house.mp3");
        Assert.Contains(vm.Tracks, t => t.Track.File.Path == "/music/deep.mp3"); // widened via GenreHierarchy
        Assert.DoesNotContain(vm.Tracks, t => t.Track.File.Path == "/music/techno.mp3");
    }

    [Fact]
    public async Task A_genre_with_no_children_toggles_exactly_like_today_and_never_touches_expansion_state()
    {
        LibrariesViewModel vm = await SeededViewModelAsync(new FakeGenreHierarchyStore(HouseWithDeepHouseChild()));

        // Expand House first, so there is expansion state that a no-children toggle must not disturb.
        vm.Genres.Single(g => g.Name == "House").IsSelected = true;
        Assert.Equal("House", vm.ExpandedParentGenre);

        vm.Genres.Single(g => g.Name == "Techno").IsSelected = true;

        // Expansion untouched by the no-children toggle.
        Assert.Equal("House", vm.ExpandedParentGenre);
        Assert.Equal(new[] { "Deep House" }, vm.ChildGenres.Select(g => g.Name));
        // Filter behaves as a plain single-level checkbox: House ∨ Techno (∨ its widened child Deep House).
        Assert.Contains(vm.Tracks, t => t.Track.File.Path == "/music/techno.mp3");
    }

    // Phase 3 — the "Group under…" curation control: GenreFilterOption.GroupUnderSelection is the combo's
    // bound property; setting it invokes GenreHierarchy.SetParent, persists via the injected store, and
    // rebuilds the picker so the new grouping is visible immediately.

    [Fact]
    public async Task Grouping_a_top_level_genre_under_another_moves_it_to_ChildGenres_and_persists()
    {
        var fakeStore = new FakeGenreHierarchyStore();
        LibrariesViewModel vm = await SeededViewModelAsync(fakeStore);

        // Techno starts top-level with no children, so its "Group under…" control offers House.
        GenreFilterOption techno = vm.Genres.Single(g => g.Name == "Techno");
        Assert.True(techno.CanGroupUnder);
        Assert.Contains("House", techno.GroupUnderOptions);

        techno.GroupUnderSelection = "House";

        // Techno is no longer offered top-level…
        Assert.DoesNotContain(vm.Genres, g => g.Name == "Techno");
        // …and shows up under House once House is expanded.
        vm.Genres.Single(g => g.Name == "House").IsSelected = true;
        Assert.Equal("House", vm.ExpandedParentGenre);
        Assert.Contains(vm.ChildGenres, g => g.Name == "Techno");

        // Persisted: the fake store's SaveAsync was called with a hierarchy reflecting the change.
        Assert.Equal(1, fakeStore.SaveCount);
        Assert.Equal("House", fakeStore.LastSaved?.ParentOf("Techno"));
    }

    [Fact]
    public async Task A_genre_that_already_has_children_never_offers_the_group_under_control()
    {
        LibrariesViewModel vm = await SeededViewModelAsync(new FakeGenreHierarchyStore(HouseWithDeepHouseChild()));

        // House already has a child (Deep House) — grouping it under something else would push Deep
        // House to a third level, so the control must be hidden rather than offered and rejected.
        GenreFilterOption house = vm.Genres.Single(g => g.Name == "House");
        Assert.False(house.CanGroupUnder);
    }

    [Fact]
    public async Task Clearing_a_grouped_genre_back_to_top_level_removes_it_from_its_parent_and_persists()
    {
        var fakeStore = new FakeGenreHierarchyStore(HouseWithDeepHouseChild());
        LibrariesViewModel vm = await SeededViewModelAsync(fakeStore);

        vm.Genres.Single(g => g.Name == "House").IsSelected = true; // expand to reach the child row
        GenreFilterOption deepHouse = vm.ChildGenres.Single(g => g.Name == "Deep House");
        Assert.Equal("House", deepHouse.GroupUnderSelection);

        deepHouse.GroupUnderSelection = GenreFilterOption.TopLevelOption;

        Assert.Empty(vm.ChildGenres.Where(g => g.Name == "Deep House"));
        Assert.Contains(vm.Genres, g => g.Name == "Deep House");
        Assert.Equal(1, fakeStore.SaveCount);
        Assert.Null(fakeStore.LastSaved?.ParentOf("Deep House"));
    }
}
