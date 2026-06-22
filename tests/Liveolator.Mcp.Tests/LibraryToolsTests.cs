using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Import;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Media;
using Liveolator.Media.Import;
using Liveolator.Mcp.Contracts;
using Liveolator.Mcp.Session;
using Liveolator.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Mcp.Tests;

public sealed class LibraryToolsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"liveolator-mcp-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ListTracks_UsesRichFiltersSortAndPaging()
    {
        LibrarySession session = await CreateSessionAsync(
            Track("a.mp3", "Alpha", "DJ One", 120, MusicMediaKind.Track),
            Track("b.mp3", "Beta", "DJ One", 130, MusicMediaKind.Sample),
            Track("c.mp3", "Gamma", "DJ Two", 125, MusicMediaKind.Sample));

        IReadOnlyList<TrackInfo> result = await LibraryTools.ListTracks(
            session,
            kind: "sample",
            artist: "DJ One",
            sort: "bpm",
            descending: true,
            limit: 1);

        TrackInfo item = Assert.Single(result);
        Assert.Equal("Beta", item.Title);
        Assert.Equal("Sample", item.Kind);
    }

    [Fact]
    public async Task ListTracks_RejectsUnknownKindWithActionableOptions()
    {
        LibrarySession session = await CreateSessionAsync(Track("a.mp3", "Alpha", null, 120));

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => LibraryTools.ListTracks(session, kind: "loop"));

        Assert.Contains("Track or Sample", error.Message);
    }

    [Fact]
    public async Task ReanalyzeTrack_RejectsUnknownCatalogPath()
    {
        LibrarySession session = await CreateSessionAsync();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => LibraryTools.ReanalyzeTrack(session, "missing.wav"));

        Assert.Contains("Scan its folder first", error.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private async Task<LibrarySession> CreateSessionAsync(params MusicTrack[] tracks)
    {
        var store = new JsonCatalogStore(_directory);
        await store.SaveMusicAsync(tracks);
        var importService = new LibraryImportService(
            new JsonHotCueStore(_directory), new JsonPlaylistStore(_directory), p => ImportFileProbe.Stat(p));
        return new LibrarySession(
            new EmptyEnumerator(),
            new EmptyDecoder(),
            new TrackAnalyzer(),
            NullTrackMetadataReader.Instance,
            store,
            Array.Empty<ILibraryImporter>(),
            Array.Empty<IFolderLibraryImporter>(),
            importService,
            NullLogger<LibrarySession>.Instance);
    }

    private static MusicTrack Track(
        string file,
        string title,
        string? artist,
        double bpm,
        MusicMediaKind kind = MusicMediaKind.Track)
        => new(
            new ScannedFile(Path.GetFullPath(file), 100, DateTime.UtcNow),
            new BpmResult(bpm, 0.9),
            new MusicalKey(0, KeyMode.Major, "8B", 0.8),
            TimeSpan.FromMinutes(4),
            TrackCues.None,
            MediaAnalysisStatus.Ok,
            null,
            TrackMetadata.Empty with { Title = title, Artist = artist, Genre = "Techno" },
            kind,
            TrackAnalyzer.CurrentVersion);

    private sealed class EmptyEnumerator : IFileEnumerator
    {
        public IEnumerable<ScannedFile> Enumerate(
            IReadOnlyList<string> folders,
            IReadOnlySet<string> extensions)
            => Array.Empty<ScannedFile>();
    }

    private sealed class EmptyDecoder : IAudioDecoder
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
