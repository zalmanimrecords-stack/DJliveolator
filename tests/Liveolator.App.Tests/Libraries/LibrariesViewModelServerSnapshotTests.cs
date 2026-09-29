using System.IO;
using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Media;
using ReactiveUI;

namespace Liveolator.App.Tests.Libraries;

/// <summary>
/// A folder the music host publishes a snapshot for takes the host's analysis; this machine must not
/// decode it, because every decode would read a whole file over the network.
/// </summary>
public sealed class LibrariesViewModelServerSnapshotTests : IDisposable
{
    private const string ServerRoot = "/media/simon/external_4tb/Navidrome/music";
    private readonly string _share = Path.Combine(Path.GetTempPath(), $"liveolator-share-{Guid.NewGuid():N}");

    public LibrariesViewModelServerSnapshotTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
        Directory.CreateDirectory(_share);
    }

    [Fact]
    public async Task Scan_TakesTheServersAnalysis_AndDecodesNothingInAServerManagedFolder()
    {
        using (var published = new SqliteCatalogStore(Path.Combine(_share, ServerSnapshotSync.SnapshotFolder)))
        {
            await published.SaveMusicAsync(new[]
            {
                new MusicTrack(new ScannedFile($"{ServerRoot}/GMS/a.flac", 4096, DateTime.UnixEpoch),
                    new BpmResult(145, 0.9), null, null, TrackCues.None, MediaAnalysisStatus.Ok, null),
            });
            await published.SaveScanFoldersAsync(new[] { ServerRoot });
        }
        string notYetOnServer = Path.Combine(_share, "new.flac");
        var library = new MusicLibrary(new FakeFileEnumerator(notYetOnServer), new FakeAudioDecoder());
        var store = new FakeMusicCatalogStore();
        var vm = new LibrariesViewModel(
            library, store: store, serverSnapshotSync: new ServerSnapshotSync(library, store));
        vm.AddFolder(_share);

        await vm.ScanCommand.Execute().ToTask();

        Assert.Equal(145, library.All.Single(t => t.File.Path == Path.Combine(_share, "GMS", "a.flac")).Bpm!.Bpm, 6);
        Assert.DoesNotContain(library.All, t => t.File.Path == notYetOnServer);
        Assert.Empty(store.SavedTrackByTrack); // per-track saves come only from a local scan
    }

    [Fact]
    public async Task Scan_StillScansAFolderWithoutASnapshotLocally()
    {
        string file = Path.Combine(_share, "local.wav");
        var library = new MusicLibrary(new FakeFileEnumerator(file), new FakeAudioDecoder());
        var store = new FakeMusicCatalogStore();
        var vm = new LibrariesViewModel(
            library, store: store, serverSnapshotSync: new ServerSnapshotSync(library, store));
        vm.AddFolder(_share);

        await vm.ScanCommand.Execute().ToTask();

        Assert.Contains(library.All, t => t.File.Path == file);
    }

    public void Dispose()
    {
        if (Directory.Exists(_share))
            Directory.Delete(_share, recursive: true);
    }
}
