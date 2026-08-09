using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Doctor;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;
using Liveolator.Core.Persistence;
using Xunit;

namespace Liveolator.Core.Tests.Library;

/// <summary>
/// The health-scan pipeline: which files get hashed, that stored hashes are reused, that identities are
/// refreshed and persisted, and that the report comes back. Extracted from <c>LibrariesViewModel</c>, so
/// these are the first tests this logic has ever had that do not need a view-model.
/// </summary>
public class LibraryHealthScannerTests
{
    private static readonly DateTime T = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ScanAsync_HashesOnlySizeCollisions()
    {
        MusicTrack[] tracks =
        [
            Track("/music/a.mp3", size: 500),
            Track("/music/b.mp3", size: 500),   // same size as a.mp3 → both are duplicate candidates
            Track("/music/lonely.mp3", size: 900), // unique size → hashing it could never change an answer
        ];
        var hasher = new RecordingHasher();

        await Scanner(hasher: hasher).ScanAsync(tracks, [], ["/music"]);

        Assert.Equal(
            ["/music/a.mp3", "/music/b.mp3"],
            hasher.Hashed.OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ScanAsync_ReusesAStoredHash_AndOnlyHashesTheMissingOne()
    {
        MusicTrack[] tracks = [Track("/music/a.mp3", size: 500), Track("/music/b.mp3", size: 500)];
        var hasher = new RecordingHasher();
        var store = new FakeIdentityStore(
            MediaIdentityBuilder.FromCatalog(
                [tracks[0]], [], T, new Dictionary<string, string?> { ["/music/a.mp3"] = "known-sha" }));

        await Scanner(store, hasher).ScanAsync(tracks, [], ["/music"]);

        Assert.Equal(["/music/b.mp3"], hasher.Hashed);
    }

    [Fact]
    public async Task ScanAsync_WithNoHasher_StillScans()
    {
        MusicTrack[] tracks = [Track("/music/a.mp3", size: 500), Track("/music/b.mp3", size: 500)];

        LibraryDoctorReport report = await Scanner(hasher: null).ScanAsync(tracks, [], ["/music"]);

        Assert.NotNull(report);
    }

    [Fact]
    public async Task ScanAsync_PersistsTheRefreshedIdentities()
    {
        MusicTrack[] tracks = [Track("/music/a.mp3", size: 500)];
        var store = new FakeIdentityStore();

        await Scanner(store).ScanAsync(tracks, [], ["/music"]);

        Assert.NotNull(store.Saved);
        Assert.Contains(store.Saved!, identity => identity.Paths.Contains("/music/a.mp3"));
    }

    [Fact]
    public async Task ScanAsync_ReportsAMissingFile()
    {
        MusicTrack[] tracks = [Track("/music/gone.mp3"), Track("/music/here.mp3")];

        LibraryDoctorReport report = await Scanner(present: ["/music/here.mp3"])
            .ScanAsync(tracks, [], ["/music"]);

        LibraryIssue issue = Assert.Single(report.Issues.Where(i => i.Kind == LibraryIssueKind.MissingFile));
        Assert.Equal("/music/gone.mp3", issue.Path);
    }

    [Fact]
    public async Task ScanAsync_HonoursCancellationDuringHashing()
    {
        MusicTrack[] tracks = [Track("/music/a.mp3", size: 500), Track("/music/b.mp3", size: 500)];
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Scanner(hasher: new RecordingHasher()).ScanAsync(tracks, [], ["/music"], cancelled.Token));
    }

    [Fact]
    public void Construction_RequiresADoctor()
        => Assert.Throws<ArgumentNullException>(() => new LibraryHealthScanner(null!));

    // --- helpers ---

    private static LibraryHealthScanner Scanner(
        IMediaIdentityStore? store = null,
        IFileContentHasher? hasher = null,
        string[]? present = null)
        => new(
            new LibraryDoctor(
                new FakeFileProbe(present ?? ["/music/a.mp3", "/music/b.mp3", "/music/lonely.mp3", "/music/here.mp3"]),
                new FakeFolderProbe("/music")),
            store,
            hasher ?? new RecordingHasher(),
            () => T);

    private static MusicTrack Track(string path, long size = 100)
        => new(
            new ScannedFile(path, size, T),
            new BpmResult(124, 0.9),
            new MusicalKey(0, KeyMode.Major, "8B", 0.9),
            TimeSpan.FromMinutes(4),
            TrackCues.None,
            MediaAnalysisStatus.Ok,
            null);

    private sealed class RecordingHasher : IFileContentHasher
    {
        public List<string> Hashed { get; } = [];

        public Task<string?> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Hashed.Add(path);
            return Task.FromResult<string?>("sha-" + path);
        }
    }

    private sealed class FakeIdentityStore : IMediaIdentityStore
    {
        private readonly IReadOnlyList<MediaIdentity> _existing;

        public FakeIdentityStore(IReadOnlyList<MediaIdentity>? existing = null)
            => _existing = existing ?? [];

        public IReadOnlyList<MediaIdentity>? Saved { get; private set; }

        public Task<IReadOnlyList<MediaIdentity>> LoadIdentitiesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_existing);

        public Task SaveIdentitiesAsync(
            IEnumerable<MediaIdentity> identities, CancellationToken cancellationToken = default)
        {
            Saved = identities.ToList();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFileProbe : IFileExistenceProbe
    {
        private readonly HashSet<string> _paths;
        public FakeFileProbe(IEnumerable<string> paths)
            => _paths = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        public bool Exists(string path) => _paths.Contains(path);
    }

    private sealed class FakeFolderProbe : IFolderExistenceProbe
    {
        private readonly HashSet<string> _folders;
        public FakeFolderProbe(params string[] folders)
            => _folders = new HashSet<string>(folders, StringComparer.OrdinalIgnoreCase);
        public bool Exists(string folder) => _folders.Contains(folder);
    }
}
