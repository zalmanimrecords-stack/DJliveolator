using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Library.Music;
using ReactiveUI;

namespace Liveolator.App.Tests.Libraries;

/// <summary>
/// Covers the "state survives a restart" requirement: the scanned catalog and the scan-folder roots
/// are persisted after a scan and restored on startup, so a fresh view-model opens where the last
/// run left off — without re-scanning or re-adding folders.
/// </summary>
public sealed class LibrariesViewModelPersistenceTests
{
    public LibrariesViewModelPersistenceTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private static MusicLibrary EmptyLibrary(params string[] files)
        => new(new FakeFileEnumerator(files), new FakeAudioDecoder());

    [Fact]
    public async Task Scan_persists_catalog_and_folders()
    {
        var store = new FakeMusicCatalogStore();
        var vm = new LibrariesViewModel(EmptyLibrary("/music/Alpha.wav", "/music/Beta.wav"), store: store);
        vm.AddFolder("/music");

        await vm.ScanCommand.Execute().ToTask();

        Assert.Equal(2, store.SavedTracks.Count);
        Assert.Contains("/music", store.SavedFolders);
        // Incremental scan (owner ask, 2026-07): each track is persisted per-row as it is analyzed, not
        // in one whole-catalog save at the end — so a mid-scan crash keeps the work already done.
        Assert.Equal(2, store.SavedTrackByTrack.Count);
        Assert.Equal(0, store.SaveMusicCalls);
    }

    [Fact]
    public async Task RemoveFolder_deletes_the_pruned_tracks_from_the_store_per_path()
    {
        var store = new FakeMusicCatalogStore();
        var vm = new LibrariesViewModel(EmptyLibrary("/music/Alpha.wav", "/music/Beta.wav"), store: store);
        vm.AddFolder("/music");
        await vm.ScanCommand.Execute().ToTask();

        vm.RemoveFolder("/music");

        // Pruned tracks are removed by explicit per-path delete (the per-row store is upsert-only, so a
        // re-save would leave their rows behind).
        Assert.Equal(2, store.DeletedPaths.Count);
        Assert.Empty(store.SavedTracks);
    }

    [Fact]
    public void AddFolder_persists_the_folder_set_immediately()
    {
        var store = new FakeMusicCatalogStore();
        var vm = new LibrariesViewModel(EmptyLibrary(), store: store);

        vm.AddFolder("/music");

        Assert.Contains("/music", store.SavedFolders);
        Assert.Equal(1, store.SaveFoldersCalls);
    }

    [Fact]
    public async Task RemoveFolder_persists_the_trimmed_folder_set_and_catalog()
    {
        var store = new FakeMusicCatalogStore();
        var vm = new LibrariesViewModel(EmptyLibrary("/music/Alpha.wav"), store: store);
        vm.AddFolder("/music");
        await vm.ScanCommand.Execute().ToTask();

        vm.RemoveFolder("/music");

        Assert.DoesNotContain("/music", store.SavedFolders); // folder root no longer persisted
        Assert.Empty(store.SavedTracks);                     // its tracks pruned from the saved catalog
    }

    [Fact]
    public async Task State_round_trips_across_view_model_instances()
    {
        // First run: scan and let it persist.
        var store = new FakeMusicCatalogStore();
        var first = new LibrariesViewModel(EmptyLibrary("/music/Alpha.wav", "/music/Beta.wav"), store: store);
        first.AddFolder("/music");
        await first.ScanCommand.Execute().ToTask();

        // Second run: a fresh library (its enumerator finds nothing) + a store seeded with what the
        // first run saved. Restore must repopulate tracks and folders from the cache alone.
        var seeded = new FakeMusicCatalogStore(store.SavedTracks, store.SavedFolders);
        using var second = new LibrariesViewModel(EmptyLibrary(), store: seeded); // dispose stops any background pass

        await second.InitializeAsync();

        Assert.Equal(2, second.Tracks.Count);
        Assert.Contains("/music", second.Folders);
    }

    [Fact]
    public async Task Initialize_without_a_store_is_a_no_op()
    {
        var vm = new LibrariesViewModel(EmptyLibrary());

        await vm.InitializeAsync(); // must not throw when persistence is disabled

        Assert.Empty(vm.Tracks);
        Assert.Empty(vm.Folders);
    }
}
