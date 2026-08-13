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
    public async Task GetTrack_FindsCatalogedTrack_ByFileName_WhenPathFormDiffers()
    {
        // An agent often has a differently-spelled path (mapped drive S:\ vs the UNC share). The exact
        // path misses, but a file-name fallback recovers the catalogued track (doc 31 L5).
        LibrarySession session = await CreateSessionAsync(Track("a.mp3", "Alpha", "DJ One", 120));

        TrackInfo info = await LibraryTools.GetTrack(session, @"\\server\share\a.mp3");

        Assert.Equal("Alpha", info.Title);
    }

    [Fact]
    public async Task ScanMusicFolders_WalksOnlyTheFoldersItIsGiven()
    {
        // The enumerator finds nothing, so every folder this scan walks loses its tracks. That makes it
        // visible which folders were walked: asking for one folder must not re-walk — or empty — another
        // library that happens to share the data root (issue #3).
        string curated = Path.Combine(_directory, "curated");
        string other = Path.Combine(_directory, "other");
        LibrarySession session = await CreateSessionAsync(
            TrackAt(Path.Combine(curated, "mine.mp3")),
            TrackAt(Path.Combine(other, "theirs.mp3")));

        ScanSummary summary = await LibraryTools.ScanMusicFolders(session, new[] { curated });

        Assert.Equal(new[] { curated }, summary.Folders);
        Assert.Contains(other, summary.KnownFolders);
        IReadOnlyList<TrackInfo> remaining = await LibraryTools.ListTracks(session);
        TrackInfo survivor = Assert.Single(remaining);
        Assert.Equal(Path.Combine(other, "theirs.mp3"), survivor.Path);
    }

    [Fact]
    public async Task SetTrackAnalysis_CorrectsTheTempo_LocksTheTrack_AndPersists()
    {
        // Issue #4: a 125 BPM record read as 168. Without this the wrong value is the catalog's forever.
        LibrarySession session = await CreateSessionAsync(Track("a.mp3", "Alpha", "DJ One", 168));

        TrackInfo corrected = await LibraryTools.SetTrackAnalysis(session, Path.GetFullPath("a.mp3"), bpm: 125);

        Assert.Equal(125, corrected.Bpm);
        Assert.True(corrected.AnalysisIsManual);
        Assert.Equal("8B", corrected.Camelot); // the unverified key is left alone

        // Survives a reload from the store, which is the whole point of the correction.
        LibrarySession reopened = ReopenSession();
        Assert.Equal(125, (await LibraryTools.GetTrack(reopened, Path.GetFullPath("a.mp3"))).Bpm);
    }

    [Fact]
    public async Task SetTrackAnalysis_RejectsAnEmptyCorrectionAndAnUnknownTrack()
    {
        LibrarySession session = await CreateSessionAsync(Track("a.mp3", "Alpha", "DJ One", 168));

        await Assert.ThrowsAsync<ArgumentException>(
            () => LibraryTools.SetTrackAnalysis(session, Path.GetFullPath("a.mp3")));
        ArgumentException missing = await Assert.ThrowsAsync<ArgumentException>(
            () => LibraryTools.SetTrackAnalysis(session, "nowhere.mp3", bpm: 125));
        Assert.Contains("Scan its folder first", missing.Message);
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
        await new JsonCatalogStore(_directory).SaveMusicAsync(tracks);
        return ReopenSession();
    }

    /// <summary>A fresh session over the same store — proves a change was persisted, not just cached.</summary>
    private LibrarySession ReopenSession()
    {
        var importService = new LibraryImportService(
            new JsonHotCueStore(_directory), new JsonPlaylistStore(_directory), p => ImportFileProbe.Stat(p));
        return new LibrarySession(
            new EmptyEnumerator(),
            new EmptyDecoder(),
            new TrackAnalyzer(),
            NullTrackMetadataReader.Instance,
            new JsonCatalogStore(_directory),
            Array.Empty<ILibraryImporter>(),
            Array.Empty<IFolderLibraryImporter>(),
            importService,
            NullLoudnessMeter.Instance,
            NullLogger<LibrarySession>.Instance);
    }

    /// <summary>A catalogued track at an exact absolute path, so folder scoping can be exercised.</summary>
    private static MusicTrack TrackAt(string fullPath)
        => new(
            new ScannedFile(fullPath, 100, DateTime.UtcNow),
            new BpmResult(128, 0.9),
            new MusicalKey(0, KeyMode.Major, "8B", 0.8),
            TimeSpan.FromMinutes(4),
            TrackCues.None,
            MediaAnalysisStatus.Ok,
            null,
            TrackMetadata.Empty with { Title = Path.GetFileNameWithoutExtension(fullPath) },
            MusicMediaKind.Track,
            TrackAnalyzer.CurrentVersion);

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
