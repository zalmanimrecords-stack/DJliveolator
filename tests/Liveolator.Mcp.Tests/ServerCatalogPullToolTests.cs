using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Import;
using Liveolator.Core.Library.Music;
using Liveolator.Media;
using Liveolator.Media.Import;
using Liveolator.Mcp.Contracts;
using Liveolator.Mcp.Session;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Mcp.Tests;

/// <summary>
/// End-to-end over two real SQLite catalogs — a scanning server's and this machine's — because the
/// value of the pull is entirely in what crosses between them. The write is opt-in: an agent that
/// calls the tool without asking for it gets a preview and the local catalog is untouched.
/// </summary>
public sealed class ServerCatalogPullToolTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"liveolator-pull-{Guid.NewGuid():N}");

    private string ServerDir => Path.Combine(_root, "server");
    private string LocalDir => Path.Combine(_root, "local");
    private string TrackPath => Path.Combine(_root, "music", "a.mp3");

    private static MusicTrack Track(string path, double? bpm)
        => new(
            new ScannedFile(path, 4096, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            bpm is null ? null : new BpmResult(bpm.Value, 0.9),
            Key: null,
            Duration: null,
            TrackCues.None,
            bpm is null ? MediaAnalysisStatus.PartiallyAnalyzed : MediaAnalysisStatus.Ok,
            Error: null);

    private async Task<LibrarySession> SeedAsync()
    {
        Directory.CreateDirectory(ServerDir);
        Directory.CreateDirectory(LocalDir);

        var server = new SqliteCatalogStore(ServerDir);
        await server.SaveMusicAsync(new[] { Track(TrackPath, bpm: 145.0) }, CancellationToken.None);

        var local = new SqliteCatalogStore(LocalDir);
        await local.SaveMusicAsync(new[] { Track(TrackPath, bpm: null) }, CancellationToken.None);

        var importService = new LibraryImportService(
            new JsonHotCueStore(LocalDir), new JsonPlaylistStore(LocalDir), p => ImportFileProbe.Stat(p));

        return new LibrarySession(
            new EmptyEnumerator(),
            new UndecodableDecoder(),
            new TrackAnalyzer(),
            NullTrackMetadataReader.Instance,
            local,
            Array.Empty<ILibraryImporter>(),
            Array.Empty<IFolderLibraryImporter>(),
            importService,
            NullLoudnessMeter.Instance,
            NullLogger<LibrarySession>.Instance);
    }

    [Fact]
    public async Task PullFromServer_PreviewsWithoutWriting()
    {
        LibrarySession session = await SeedAsync();

        ServerPullSummaryDto preview = await session.PullFromServerAsync(
            ServerDir, null, null, apply: false, CancellationToken.None);

        Assert.False(preview.Applied);
        Assert.Equal(1, preview.TracksGainingAnalysis);

        // The local catalog on disk must be exactly as it was — a preview that writes is not a preview.
        IReadOnlyList<MusicTrack> onDisk =
            await new SqliteCatalogStore(LocalDir).LoadMusicAsync(CancellationToken.None);
        Assert.Null(Assert.Single(onDisk).Bpm);
    }

    [Fact]
    public async Task PullFromServer_WritesTheServersGrid_WhenApplyIsAsked()
    {
        LibrarySession session = await SeedAsync();

        ServerPullSummaryDto applied = await session.PullFromServerAsync(
            ServerDir, null, null, apply: true, CancellationToken.None);

        Assert.True(applied.Applied);
        Assert.Equal(1, applied.TracksGainingAnalysis);

        IReadOnlyList<MusicTrack> onDisk =
            await new SqliteCatalogStore(LocalDir).LoadMusicAsync(CancellationToken.None);
        Assert.Equal(145.0, Assert.Single(onDisk).Bpm!.Bpm, 6);
    }

    [Fact]
    public async Task PullFromServer_RefusesADirectoryThatIsNotThere()
    {
        LibrarySession session = await SeedAsync();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => session.PullFromServerAsync(
                Path.Combine(_root, "nope"), null, null, apply: false, CancellationToken.None));

        // The message has to name the fix, because an agent cannot see the filesystem to work it out.
        Assert.Contains("catalog.db", error.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class EmptyEnumerator : IFileEnumerator
    {
        public IEnumerable<ScannedFile> Enumerate(
            IReadOnlyList<string> folders, IReadOnlySet<string> extensions)
            => Array.Empty<ScannedFile>();
    }

    private sealed class UndecodableDecoder : IAudioDecoder
    {
        public bool CanDecode(string filePath) => false;

        public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
            string filePath,
            int targetSampleRate,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
