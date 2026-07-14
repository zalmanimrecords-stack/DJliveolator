using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading.Tasks;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Analysis;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Libraries;

/// <summary>
/// B2 — designating a scan folder as a "samples" source from the Folders window. Toggling a folder
/// must reclassify the catalog (Track ↔ Sample), persist the designation, and survive a restart.
/// The catalog is seeded with full-length (4-minute) tracks so the folder override — not the
/// short-clip duration heuristic — is what flips the kind.
/// </summary>
public sealed class LibrariesViewModelSampleFolderTests : IDisposable
{
    // Seeded VMs start a background re-analysis pass; dispose them after each test so it never leaks into
    // a later test and mutates UI state on a background thread (doc 27 B0).
    private readonly List<IDisposable> _created = new();

    public LibrariesViewModelSampleFolderTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    public void Dispose()
    {
        foreach (IDisposable vm in _created)
            vm.Dispose();
    }

    private static MusicTrack FullTrack(string path) => new(
        new ScannedFile(path, 100, DateTime.UtcNow),
        null, null, TimeSpan.FromMinutes(4), TrackCues.None,
        MediaAnalysisStatus.Ok, null, null, MusicMediaKind.Track);

    private static MusicLibrary EmptyLibrary()
        => new(new FakeFileEnumerator(), new FakeAudioDecoder());

    private async Task<(LibrariesViewModel vm, FakeMusicCatalogStore store)> SeededAsync(
        string[]? sampleFolders = null)
    {
        var store = new FakeMusicCatalogStore(
            seedTracks: new[] { FullTrack("/loops/break.wav") },
            seedFolders: new[] { "/loops" },
            seedSampleFolders: sampleFolders);
        var vm = new LibrariesViewModel(EmptyLibrary(), store: store);
        _created.Add(vm);
        await vm.InitializeAsync();
        return (vm, store);
    }

    [Fact]
    public async Task Toggling_a_folder_as_samples_persists_and_reclassifies()
    {
        (LibrariesViewModel vm, FakeMusicCatalogStore store) = await SeededAsync();

        FolderStatusViewModel folder = vm.FolderStatuses.Single();
        Assert.False(folder.IsSampleFolder); // a normal folder by default

        folder.IsSampleFolder = true;

        Assert.Contains("/loops", store.SavedSampleFolders);
        Assert.True(store.SaveSampleFoldersCalls >= 1);
        // The library reclassified the cached track to a Sample (no re-decode).
        Assert.All(vm.Tracks, t => Assert.Equal(MusicMediaKind.Sample, t.Track.Kind));
    }

    [Fact]
    public async Task Untoggling_a_sample_folder_reclassifies_back_to_tracks()
    {
        (LibrariesViewModel vm, FakeMusicCatalogStore store) = await SeededAsync(sampleFolders: new[] { "/loops" });

        FolderStatusViewModel folder = vm.FolderStatuses.Single();
        Assert.True(folder.IsSampleFolder); // restored as a sample source

        folder.IsSampleFolder = false;

        Assert.DoesNotContain("/loops", store.SavedSampleFolders);
        Assert.All(vm.Tracks, t => Assert.Equal(MusicMediaKind.Track, t.Track.Kind));
    }

    [Fact]
    public async Task Sample_designation_is_applied_to_a_restored_catalog_at_startup()
    {
        (LibrariesViewModel vm, _) = await SeededAsync(sampleFolders: new[] { "/loops" });

        Assert.True(vm.FolderStatuses.Single().IsSampleFolder);
        Assert.Equal(MusicMediaKind.Sample, vm.Tracks.Single().Track.Kind);
    }
}
