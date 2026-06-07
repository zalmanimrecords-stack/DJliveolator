using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;
using Xunit;

namespace Liveolator.Media.Tests;

/// <summary>
/// Behaviour of the single SQLite-backed catalog gateway (<see cref="SqliteCatalogStore"/>): it must be a
/// drop-in for <see cref="JsonCatalogStore"/> — round-tripping the full analyzed model and the folder
/// lists, replacing the catalog wholesale on save, and degrading to an empty result (never throwing) when
/// the database file is unreadable (global standards #16/#26).
/// </summary>
public class SqliteCatalogStoreTests
{
    private static string DbPath(TempDirectory dir) => System.IO.Path.Combine(dir.Path, "catalog.db");

    [Fact]
    public async Task SaveThenLoad_RoundTripsAnalyzedAndFailedTracks()
    {
        using var dir = new TempDirectory();
        var store = new SqliteCatalogStore(DbPath(dir));
        var tracks = new[]
        {
            TestTracks.Analyzed("c.wav", 124.0, tonic: 0, mode: KeyMode.Major), // 8B
            TestTracks.Failed("broken.mp3"),
        };

        await store.SaveMusicAsync(tracks);
        IReadOnlyList<MusicTrack> loaded = await store.LoadMusicAsync();

        Assert.Equal(2, loaded.Count);
        MusicTrack c = loaded.Single(t => t.File.Path == "c.wav");
        Assert.Equal(124.0, c.Bpm!.Bpm);
        Assert.Equal("8B", c.Key!.Camelot);
        Assert.Equal(KeyMode.Major, c.Key.Mode);
        Assert.Equal(MediaAnalysisStatus.Ok, c.Status);
        Assert.NotNull(c.Cues.IntroStart);

        MusicTrack broken = loaded.Single(t => t.File.Path == "broken.mp3");
        Assert.Equal(MediaAnalysisStatus.Failed, broken.Status);
        Assert.Equal("decode error", broken.Error);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsKind()
    {
        using var dir = new TempDirectory();
        var store = new SqliteCatalogStore(DbPath(dir));
        var sample = TestTracks.Analyzed("loop.wav", 120.0, 0, KeyMode.Major, kind: MusicMediaKind.Sample);
        var song = TestTracks.Analyzed("song.wav", 128.0, 0, KeyMode.Major, kind: MusicMediaKind.Track);

        await store.SaveMusicAsync(new[] { sample, song });
        IReadOnlyList<MusicTrack> loaded = await store.LoadMusicAsync();

        Assert.Equal(MusicMediaKind.Sample, loaded.Single(t => t.File.Path == "loop.wav").Kind);
        Assert.Equal(MusicMediaKind.Track, loaded.Single(t => t.File.Path == "song.wav").Kind);
    }

    [Fact]
    public async Task SaveMusic_ReplacesPreviousCatalog_NoStaleRows()
    {
        using var dir = new TempDirectory();
        var store = new SqliteCatalogStore(DbPath(dir));

        await store.SaveMusicAsync(new[]
        {
            TestTracks.Analyzed("a.wav", 120.0, 0, KeyMode.Major),
            TestTracks.Analyzed("b.wav", 122.0, 0, KeyMode.Major),
        });
        await store.SaveMusicAsync(new[] { TestTracks.Analyzed("a.wav", 130.0, 0, KeyMode.Major) });

        IReadOnlyList<MusicTrack> loaded = await store.LoadMusicAsync();
        MusicTrack only = Assert.Single(loaded);
        Assert.Equal("a.wav", only.File.Path);
        Assert.Equal(130.0, only.Bpm!.Bpm); // the row was replaced, not duplicated
    }

    [Fact]
    public async Task LoadMusic_WhenEmpty_ReturnsEmpty()
    {
        using var dir = new TempDirectory();
        var store = new SqliteCatalogStore(DbPath(dir));
        Assert.Empty(await store.LoadMusicAsync());
    }

    [Fact]
    public async Task ScanFolders_RoundTrip_PreservesOrder()
    {
        using var dir = new TempDirectory();
        var store = new SqliteCatalogStore(DbPath(dir));

        await store.SaveScanFoldersAsync(new[] { "/m/b", "/m/a", "/m/c" });
        IReadOnlyList<string> loaded = await store.LoadScanFoldersAsync();

        Assert.Equal(new[] { "/m/b", "/m/a", "/m/c" }, loaded);
    }

    [Fact]
    public async Task SampleFolders_RoundTrip()
    {
        using var dir = new TempDirectory();
        var store = new SqliteCatalogStore(DbPath(dir));

        await store.SaveSampleFoldersAsync(new[] { "/m/loops", "/m/oneshots" });
        Assert.Equal(new[] { "/m/loops", "/m/oneshots" }, await store.LoadSampleFoldersAsync());
    }

    [Fact]
    public async Task FolderScopes_AreIndependent()
    {
        using var dir = new TempDirectory();
        var store = new SqliteCatalogStore(DbPath(dir));

        await store.SaveScanFoldersAsync(new[] { "/music" });
        await store.SaveSampleFoldersAsync(new[] { "/samples" });
        await store.SaveVisualScanFoldersAsync(new[] { "/clips" });

        Assert.Equal(new[] { "/music" }, await store.LoadScanFoldersAsync());
        Assert.Equal(new[] { "/samples" }, await store.LoadSampleFoldersAsync());
        Assert.Equal(new[] { "/clips" }, await store.LoadVisualScanFoldersAsync());
    }

    [Fact]
    public async Task VisualAssets_RoundTrip()
    {
        using var dir = new TempDirectory();
        var store = new SqliteCatalogStore(DbPath(dir));

        await store.SaveVisualAsync(new[]
        {
            TestTracks.Video("v.mp4", 1920, 1080, 12.5),
            TestTracks.Image("i.png", 320, 240),
        });
        IReadOnlyList<VisualAsset> loaded = await store.LoadVisualAsync();

        Assert.Equal(2, loaded.Count);
        VisualAsset video = loaded.Single(a => a.File.Path == "v.mp4");
        Assert.Equal(VisualMediaKind.Video, video.Kind);
        Assert.Equal(1920, video.Info!.Value.Width);
    }

    [Fact]
    public async Task DatabaseFile_IsCreatedOnSave()
    {
        using var dir = new TempDirectory();
        string path = DbPath(dir);
        var store = new SqliteCatalogStore(path);

        await store.SaveScanFoldersAsync(new[] { "/m" });

        Assert.True(File.Exists(path)); // a single DB file is the whole persisted store
    }

    [Fact]
    public async Task LoadMusic_WhenDatabaseUnreadable_ReturnsEmptyAndWarns()
    {
        using var dir = new TempDirectory();
        string path = DbPath(dir);
        await File.WriteAllTextAsync(path, "this is not a sqlite database"); // corrupt/garbage file
        string? warning = null;
        var store = new SqliteCatalogStore(path, onWarning: w => warning = w);

        IReadOnlyList<MusicTrack> loaded = await store.LoadMusicAsync();

        Assert.Empty(loaded);
        Assert.NotNull(warning);
    }
}
