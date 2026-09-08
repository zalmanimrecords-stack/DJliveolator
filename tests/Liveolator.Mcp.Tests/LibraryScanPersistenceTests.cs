using Liveolator.Core.Analysis;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Import;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Media;
using Liveolator.Media.Import;
using Liveolator.Mcp.Session;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Mcp.Tests;

/// <summary>
/// A scan must persist every track the moment it is analyzed, not only in one whole-catalog save at
/// the end. Without that, a scan cut short — a client timeout, a dropped network share — discarded
/// everything it had already analyzed, which on a large SMB library means hours of work lost.
/// </summary>
public sealed class LibraryScanPersistenceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"liveolator-scan-persist-{Guid.NewGuid():N}");

    [Fact]
    public async Task ScanAsync_PersistsEachTrackAsItIsAnalyzed()
    {
        var store = new RecordingCatalogStore();
        LibrarySession session = CreateSession(store, "one.mp3", "two.mp3");

        await session.ScanAsync(new[] { _directory }, force: false, CancellationToken.None);

        Assert.Equal(
            new[] { Path.Combine(_directory, "one.mp3"), Path.Combine(_directory, "two.mp3") },
            store.SavedTrackPaths.Order().ToArray());
    }

    [Fact]
    public async Task ScanAsync_KeepsTheTracksItAlreadyAnalyzed_WhenTheScanIsCutShort()
    {
        // Fails the write for the second track, standing in for a timeout or a share going away
        // mid-scan: whatever was already analyzed must still have reached the store.
        var store = new RecordingCatalogStore { ThrowOnSaveOfPathEndingWith = "two.mp3" };
        LibrarySession session = CreateSession(store, "one.mp3", "two.mp3");

        await Assert.ThrowsAsync<IOException>(
            () => session.ScanAsync(new[] { _directory }, force: false, CancellationToken.None));

        Assert.Contains(Path.Combine(_directory, "one.mp3"), store.SavedTrackPaths);
    }

    private LibrarySession CreateSession(IMusicCatalogStore store, params string[] fileNames)
    {
        Directory.CreateDirectory(_directory);
        var importService = new LibraryImportService(
            new JsonHotCueStore(_directory), new JsonPlaylistStore(_directory), p => ImportFileProbe.Stat(p));
        return new LibrarySession(
            new StubEnumerator(_directory, fileNames),
            new SilentDecoder(),
            new TrackAnalyzer(),
            NullTrackMetadataReader.Instance,
            store,
            Array.Empty<ILibraryImporter>(),
            Array.Empty<IFolderLibraryImporter>(),
            importService,
            NullLoudnessMeter.Instance,
            NullLogger<LibrarySession>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Yields a fixed set of files so a scan runs without touching real audio.</summary>
    private sealed class StubEnumerator(string directory, string[] fileNames) : IFileEnumerator
    {
        public IEnumerable<ScannedFile> Enumerate(
            IReadOnlyList<string> folders, IReadOnlySet<string> extensions)
            => fileNames.Select(n =>
                new ScannedFile(Path.Combine(directory, n), 1024, new DateTime(2026, 1, 1)));
    }

    /// <summary>Decodes nothing; the analyzer records the entry as unanalyzable, which still persists.</summary>
    private sealed class SilentDecoder : IAudioDecoder
    {
        public bool CanDecode(string filePath) => false;

        public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
            string filePath,
            int targetSampleRate,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    /// <summary>Records the per-track writes, and can fail one to model a scan cut short.</summary>
    private sealed class RecordingCatalogStore : IMusicCatalogStore
    {
        public List<string> SavedTrackPaths { get; } = new();

        public string? ThrowOnSaveOfPathEndingWith { get; init; }

        public Task SaveTrackAsync(MusicTrack track, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSaveOfPathEndingWith is { } stop && track.File.Path.EndsWith(stop, StringComparison.Ordinal))
                throw new IOException("the share went away");
            SavedTrackPaths.Add(track.File.Path);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MusicTrack>> LoadMusicAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MusicTrack>>(Array.Empty<MusicTrack>());

        public Task SaveMusicAsync(
            IEnumerable<MusicTrack> tracks, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteTrackAsync(string path, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<string>> LoadScanFoldersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task SaveScanFoldersAsync(
            IEnumerable<string> folders, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<string>> LoadSampleFoldersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task SaveSampleFoldersAsync(
            IEnumerable<string> folders, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
