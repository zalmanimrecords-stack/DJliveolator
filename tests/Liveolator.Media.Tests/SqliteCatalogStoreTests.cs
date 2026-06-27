using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;
using Xunit;

namespace Liveolator.Media.Tests;

/// <summary>
/// SQLite catalog store (doc 31 step 10): per-row upsert + WAL replace the whole-file JSON rewrite,
/// fixing the O(catalog) save and the App↔MCP cross-process last-writer-wins race (M1).
/// </summary>
public sealed class SqliteCatalogStoreTests
{
    [Fact]
    public async Task SaveThenLoad_RoundTripsTracks()
    {
        using var dir = new TempDirectory();
        using var store = new SqliteCatalogStore(dir.Path);

        await store.SaveMusicAsync(new[]
        {
            TestTracks.Analyzed("c.wav", 124.0, tonic: 0, mode: KeyMode.Major),
            TestTracks.Failed("broken.mp3"),
        });
        IReadOnlyList<MusicTrack> loaded = await store.LoadMusicAsync();

        Assert.Equal(2, loaded.Count);
        MusicTrack c = loaded.Single(t => t.File.Path == "c.wav");
        Assert.Equal(124.0, c.Bpm!.Bpm);
        Assert.Equal("8B", c.Key!.Camelot);
        Assert.Equal(MediaAnalysisStatus.Ok, c.Status);
        Assert.Equal(TrackAnalyzer.CurrentVersion, c.AnalyzerVersion);
        Assert.Equal("decode error", loaded.Single(t => t.File.Path == "broken.mp3").Error);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsBeatGridDownbeat()
    {
        using var dir = new TempDirectory();
        using var store = new SqliteCatalogStore(dir.Path);
        MusicTrack track = TestTracks.Analyzed("grid.wav", 128.0, tonic: 0, mode: KeyMode.Major) with
        {
            Bpm = new BpmResult(128.0, 0.9, FirstBeatSeconds: 0.05)
            {
                DownbeatSeconds = 0.55,
                BeatsPerBar = 4,
                DownbeatConfidence = 0.62,
            },
        };

        await store.SaveMusicAsync(new[] { track });
        MusicTrack loaded = (await store.LoadMusicAsync()).Single();

        Assert.Equal(0.55, loaded.Bpm!.DownbeatSeconds, 6);
        Assert.Equal(0.05, loaded.Bpm.FirstBeatSeconds, 6);
        Assert.Equal(0.62, loaded.Bpm.DownbeatConfidence, 6);
    }

    [Fact]
    public async Task SaveMusic_UpsertsByPath_NotDuplicate()
    {
        using var dir = new TempDirectory();
        using var store = new SqliteCatalogStore(dir.Path);

        await store.SaveMusicAsync(new[] { TestTracks.Analyzed("a.wav", 120.0, 0, KeyMode.Major) });
        await store.SaveMusicAsync(new[] { TestTracks.Analyzed("a.wav", 140.0, 0, KeyMode.Major) });

        MusicTrack loaded = Assert.Single(await store.LoadMusicAsync());
        Assert.Equal(140.0, loaded.Bpm!.Bpm); // updated in place, not duplicated
    }

    [Fact]
    public async Task Load_EmptyDatabase_ReturnsEmpty()
    {
        using var dir = new TempDirectory();
        using var store = new SqliteCatalogStore(dir.Path);

        Assert.Empty(await store.LoadMusicAsync());
    }

    [Fact]
    public async Task DeleteTrack_RemovesItFromTheCatalog()
    {
        using var dir = new TempDirectory();
        using var store = new SqliteCatalogStore(dir.Path);
        await store.SaveMusicAsync(new[]
        {
            TestTracks.Analyzed("keep.wav", 120.0, 0, KeyMode.Major),
            TestTracks.Analyzed("drop.wav", 120.0, 0, KeyMode.Major),
        });

        await store.DeleteTrackAsync("drop.wav");

        MusicTrack loaded = Assert.Single(await store.LoadMusicAsync());
        Assert.Equal("keep.wav", loaded.File.Path);
    }

    [Fact]
    public async Task TwoStoresOnTheSameDatabase_DoNotClobberEachOthersRows()
    {
        // The App and the MCP server are two processes over one catalog. Each upserts only its own view;
        // upsert-only (no delete-missing) means neither drops the other's track — the M1 race is gone.
        using var dir = new TempDirectory();
        using var app = new SqliteCatalogStore(dir.Path);
        using var mcp = new SqliteCatalogStore(dir.Path);

        await app.SaveMusicAsync(new[] { TestTracks.Analyzed("from-app.wav", 120.0, 0, KeyMode.Major) });
        await mcp.SaveMusicAsync(new[] { TestTracks.Analyzed("from-mcp.wav", 130.0, 0, KeyMode.Major) });

        IReadOnlyList<MusicTrack> loaded = await app.LoadMusicAsync();
        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, t => t.File.Path == "from-app.wav");
        Assert.Contains(loaded, t => t.File.Path == "from-mcp.wav");
    }

    [Fact]
    public async Task VisualAssets_RoundTrip()
    {
        using var dir = new TempDirectory();
        using var store = new SqliteCatalogStore(dir.Path);

        await store.SaveVisualAsync(new[]
        {
            TestTracks.Video("clip.mp4", 1920, 1080, 12.0),
            TestTracks.Image("logo.png", 512, 512),
        });
        IReadOnlyList<VisualAsset> loaded = await store.LoadVisualAsync();

        Assert.Equal(2, loaded.Count);
        Assert.Equal(VisualMediaKind.Video, loaded.Single(a => a.File.Path == "clip.mp4").Kind);
        Assert.Equal(512, loaded.Single(a => a.File.Path == "logo.png").Info!.Value.Width);
    }

    [Fact]
    public async Task ScanSampleAndVisualFolders_RoundTripIndependently()
    {
        using var dir = new TempDirectory();
        using var store = new SqliteCatalogStore(dir.Path);

        await store.SaveScanFoldersAsync(new[] { "/music/a", "/music/b" });
        await store.SaveSampleFoldersAsync(new[] { "/music/loops" });
        await store.SaveVisualScanFoldersAsync(new[] { "/visuals" });

        Assert.Equal(new[] { "/music/a", "/music/b" }, await store.LoadScanFoldersAsync());
        Assert.Equal(new[] { "/music/loops" }, await store.LoadSampleFoldersAsync());
        Assert.Equal(new[] { "/visuals" }, await store.LoadVisualScanFoldersAsync());
    }

    [Fact]
    public async Task SaveScanFolders_ReplacesThePreviousList()
    {
        using var dir = new TempDirectory();
        using var store = new SqliteCatalogStore(dir.Path);

        await store.SaveScanFoldersAsync(new[] { "/old" });
        await store.SaveScanFoldersAsync(new[] { "/new1", "/new2" });

        Assert.Equal(new[] { "/new1", "/new2" }, await store.LoadScanFoldersAsync());
    }
}
