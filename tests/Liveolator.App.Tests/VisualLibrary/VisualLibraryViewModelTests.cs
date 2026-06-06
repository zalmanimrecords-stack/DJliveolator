using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Liveolator.App.Features.VisualLibrary;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Visual;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.VisualLibrary;

/// <summary>
/// Track C C1 — the VJ / Visual Library tab. Drives scan, restore, and the kind/status/text filter
/// surface over the real Core <see cref="VisualMediaLibrary"/> (with fake enumerator + probe) and the
/// persistence store, asserting the visible <see cref="VisualLibraryViewModel.Assets"/> narrows and
/// that scan/folder state is persisted.
/// </summary>
public sealed class VisualLibraryViewModelTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public VisualLibraryViewModelTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private static VisualAsset Asset(string path, VisualMediaKind kind, MediaAnalysisStatus status = MediaAnalysisStatus.Ok)
        => new(
            new ScannedFile(path, 1000, T), kind,
            kind == VisualMediaKind.Video ? new VisualMediaInfo(1920, 1080, TimeSpan.FromSeconds(10)) : new VisualMediaInfo(800, 600, null),
            status, status == MediaAnalysisStatus.Failed ? "boom" : null);

    private static readonly VisualAsset[] Catalog =
    {
        Asset("/vis/sunset.jpg", VisualMediaKind.Image),
        Asset("/vis/loop.mp4", VisualMediaKind.Video),
        Asset("/vis/grid.png", VisualMediaKind.Image),
        Asset("/vis/broken.gif", VisualMediaKind.Image, MediaAnalysisStatus.Failed),
    };

    private static VisualMediaLibrary EmptyLibrary(params string[] files)
        => new(new FakeFileEnumerator(files), new FakeVisualMediaProbe());

    private static async Task<VisualLibraryViewModel> RestoredAsync(FakeVisualCatalogStore store)
    {
        var vm = new VisualLibraryViewModel(EmptyLibrary(), store);
        await vm.InitializeAsync();
        return vm;
    }

    [Fact]
    public async Task Initialize_restores_folders_and_catalog()
    {
        var store = new FakeVisualCatalogStore(seedAssets: Catalog, seedFolders: new[] { "/vis" });

        VisualLibraryViewModel vm = await RestoredAsync(store);

        Assert.Contains("/vis", vm.Folders);
        Assert.Equal(4, vm.Assets.Count);
        Assert.Contains("restored", vm.ScanStatus);
    }

    [Fact]
    public async Task Kind_filter_narrows_to_videos()
    {
        VisualLibraryViewModel vm = await RestoredAsync(new FakeVisualCatalogStore(seedAssets: Catalog));

        vm.SelectedKind = VisualMediaKind.Video;

        VisualAssetRowViewModel only = Assert.Single(vm.Assets);
        Assert.Equal("loop", only.Title);
        Assert.Equal("Video", only.KindText);
    }

    [Fact]
    public async Task Status_filter_narrows_to_failed()
    {
        VisualLibraryViewModel vm = await RestoredAsync(new FakeVisualCatalogStore(seedAssets: Catalog));

        vm.SelectedStatus = MediaAnalysisStatus.Failed;

        Assert.Equal("broken", Assert.Single(vm.Assets).Title);
    }

    [Fact]
    public async Task Search_composes_with_the_kind_filter()
    {
        VisualLibraryViewModel vm = await RestoredAsync(new FakeVisualCatalogStore(seedAssets: Catalog));

        vm.SelectedKind = VisualMediaKind.Image;
        vm.SearchText = "loop"; // loop is a Video, so the combination is empty

        Assert.Empty(vm.Assets);
    }

    [Fact]
    public async Task ClearFilters_resets_kind_status_search_and_shows_all()
    {
        VisualLibraryViewModel vm = await RestoredAsync(new FakeVisualCatalogStore(seedAssets: Catalog));
        vm.SelectedKind = VisualMediaKind.Video;
        vm.SelectedStatus = MediaAnalysisStatus.Ok;
        vm.SearchText = "zzz";

        vm.ClearFiltersCommand.Execute().Subscribe();

        Assert.Null(vm.SelectedKind);
        Assert.Null(vm.SelectedStatus);
        Assert.True(string.IsNullOrEmpty(vm.SearchText));
        Assert.Equal(4, vm.Assets.Count);
    }

    [Fact]
    public async Task Scan_probes_files_persists_catalog_and_folders()
    {
        var store = new FakeVisualCatalogStore();
        var library = EmptyLibrary("/vis/a.png", "/vis/b.mp4");
        var vm = new VisualLibraryViewModel(library, store);
        vm.AddFolder("/vis");

        await vm.ScanCommand.Execute().ToTask();

        Assert.Equal(2, vm.Assets.Count);
        Assert.Equal("2 assets", vm.ScanStatus);
        // The video carries a duration; the image does not.
        Assert.Equal("0:12", vm.Assets.Single(a => a.KindText == "Video").Duration);
        Assert.Equal("—", vm.Assets.Single(a => a.KindText == "Image").Duration);
        // Persisted: catalog + scan folders.
        Assert.True(store.SaveAssetsCalls >= 1);
        Assert.Contains("/vis", store.SavedFolders);
    }

    [Fact]
    public void AddFolder_persists_the_folder_set_before_a_scan()
    {
        var store = new FakeVisualCatalogStore();
        var vm = new VisualLibraryViewModel(EmptyLibrary(), store);

        vm.AddFolder("/vis/one");
        vm.AddFolder("/vis/one"); // duplicate is a no-op

        Assert.Single(vm.Folders);
        Assert.Contains("/vis/one", store.SavedFolders);
    }

    [Fact]
    public async Task Failed_probe_is_a_failed_asset_not_a_crash()
    {
        var store = new FakeVisualCatalogStore();
        var probe = new FakeVisualMediaProbe();
        probe.FailPaths.Add("/vis/bad.png");
        var library = new VisualMediaLibrary(new FakeFileEnumerator("/vis/good.jpg", "/vis/bad.png"), probe);
        var vm = new VisualLibraryViewModel(library, store);
        vm.AddFolder("/vis");

        await vm.ScanCommand.Execute().ToTask();

        Assert.Equal(2, vm.Assets.Count);
        Assert.Equal(MediaAnalysisStatus.Failed, vm.Assets.Single(a => a.Title == "bad").Status);
    }
}
