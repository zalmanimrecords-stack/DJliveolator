using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Xunit;

namespace Liveolator.Media.Tests;

/// <summary>
/// The music host scans its own disk and publishes <c>&lt;root&gt;/.liveolator/catalog.db</c>; a PC adopts that
/// analysis for the same folder instead of decoding every file over the network.
/// </summary>
public sealed class ServerSnapshotSyncTests : IDisposable
{
    private const string ServerRoot = "/media/simon/external_4tb/Navidrome/music";
    private readonly TempDirectory _share = new();
    private readonly TempDirectory _local = new();

    private string Root => _share.Path;

    private static MusicTrack Track(string path, double? bpm = null)
        => new(new ScannedFile(path, 4096, new DateTime(2026, 6, 27, 13, 11, 55, DateTimeKind.Utc)),
            bpm is null ? null : new BpmResult(bpm.Value, 0.9), Key: null, Duration: null, TrackCues.None,
            bpm is null ? MediaAnalysisStatus.PartiallyAnalyzed : MediaAnalysisStatus.Ok, Error: null);

    private async Task PublishAsync(IEnumerable<MusicTrack> serverTracks, string? writerRoot = ServerRoot)
    {
        string dir = Path.Combine(Root, ServerSnapshotSync.SnapshotFolder);
        using var store = new SqliteCatalogStore(dir);
        await store.SaveMusicAsync(serverTracks);
        if (writerRoot is not null)
            await store.SaveScanFoldersAsync(new[] { writerRoot });
    }

    private (ServerSnapshotSync Sync, MusicLibrary Library, SqliteCatalogStore Store, List<string> Warnings) Build(
        Func<string, bool>? fileExists = null, params MusicTrack[] local)
    {
        var library = new MusicLibrary(new NoFiles(), new NoDecoder());
        library.Restore(local);
        var store = new SqliteCatalogStore(_local.Path);
        var warnings = new List<string>();
        return (new ServerSnapshotSync(library, store, fileExists ?? (_ => true), warnings.Add), library, store, warnings);
    }

    [Fact]
    public async Task Sync_AdoptsTheServersAnalysis_RebasedOntoTheLocalRoot_AndPersistsIt()
    {
        await PublishAsync(new[] { Track($"{ServerRoot}/GMS/a.flac", bpm: 145) });
        var (sync, library, store, _) = Build();

        ServerSnapshotSyncResult result = await sync.SyncAsync(new[] { Root });

        Assert.Equal(new[] { Root }, result.ManagedRoots);
        string expected = Path.Combine(Root, "GMS", "a.flac");
        Assert.Equal(145, library.All.Single(t => t.File.Path == expected).Bpm!.Bpm, 6);
        Assert.Single(await store.LoadMusicAsync());
    }

    [Fact]
    public async Task Sync_LeavesAFolderWithoutASnapshotToTheLocalScan()
    {
        var (sync, library, _, _) = Build(null, Track(Path.Combine(Root, "x.mp3")));

        ServerSnapshotSyncResult result = await sync.SyncAsync(new[] { Root });

        Assert.Empty(result.ManagedRoots);
        Assert.Single(library.All);
    }

    [Fact]
    public async Task Sync_IgnoresASnapshotThatDoesNotSayWhichFolderItScanned()
    {
        await PublishAsync(new[] { Track($"{ServerRoot}/a.flac", bpm: 145) }, writerRoot: null);
        var (sync, library, _, warnings) = Build();

        ServerSnapshotSyncResult result = await sync.SyncAsync(new[] { Root });

        Assert.Empty(result.ManagedRoots);
        Assert.Empty(library.All);
        Assert.Contains(warnings, w => w.Contains("scan folder"));
    }

    [Fact]
    public async Task Sync_DropsARowTheServerNoLongerHas_OnlyWhenTheFileIsGone()
    {
        await PublishAsync(new[] { Track($"{ServerRoot}/kept.flac", bpm: 140) });
        string gone = Path.Combine(Root, "deleted.flac");
        string stillThere = Path.Combine(Root, "not-scanned-yet.flac");
        var (sync, library, _, _) = Build(path => path != gone, Track(gone), Track(stillThere));

        await sync.SyncAsync(new[] { Root });

        string[] paths = library.All.Select(t => t.File.Path).ToArray();
        Assert.DoesNotContain(gone, paths);
        Assert.Contains(stillThere, paths);
    }

    [Fact]
    public async Task Sync_TreatsADamagedSnapshotAsNoSnapshot_AndSaysSo()
    {
        string dir = Path.Combine(Root, ServerSnapshotSync.SnapshotFolder);
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, "catalog.db"), new byte[8192]);
        var (sync, library, _, warnings) = Build(null, Track(Path.Combine(Root, "x.mp3")));

        ServerSnapshotSyncResult result = await sync.SyncAsync(new[] { Root });

        Assert.Empty(result.ManagedRoots);
        Assert.Single(library.All);
        Assert.NotEmpty(warnings);
    }

    public void Dispose()
    {
        _share.Dispose();
        _local.Dispose();
    }

    private sealed class NoFiles : IFileEnumerator
    {
        public IEnumerable<ScannedFile> Enumerate(IReadOnlyList<string> folders, IReadOnlySet<string> extensions)
            => Array.Empty<ScannedFile>();
    }

    private sealed class NoDecoder : IAudioDecoder
    {
        public bool CanDecode(string filePath) => false;

        public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
            string filePath, int targetSampleRate,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
