using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Analysis.Structure;
using Liveolator.Core.Enrichment;
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
    public async Task SaveTrack_UpsertsOneRow_ForTheIncrementalScan()
    {
        using var dir = new TempDirectory();
        using var store = new SqliteCatalogStore(dir.Path);

        // Each scanned track persisted on its own, then one updated in place — the incremental scan write.
        await store.SaveTrackAsync(TestTracks.Analyzed("a.wav", 120.0, 0, KeyMode.Major));
        await store.SaveTrackAsync(TestTracks.Analyzed("b.wav", 128.0, 0, KeyMode.Major));
        await store.SaveTrackAsync(TestTracks.Analyzed("a.wav", 140.0, 0, KeyMode.Major));

        IReadOnlyList<MusicTrack> loaded = await store.LoadMusicAsync();
        Assert.Equal(2, loaded.Count);
        Assert.Equal(140.0, loaded.Single(t => t.File.Path == "a.wav").Bpm!.Bpm);
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

    // The catalog stores each track as ONE JSON blob, so every field rides in the same column and a
    // missing serializer option loses ALL of them at once, silently. These are the fields a server
    // pull (or any merge) must never destroy, so they are pinned here rather than assumed.
    [Fact]
    public async Task SaveThenLoad_RoundTripsUserAndLibraryFields()
    {
        using var dir = new TempDirectory();
        using var store = new SqliteCatalogStore(dir.Path);
        var added = new DateTime(2023, 5, 4, 9, 30, 0, DateTimeKind.Utc);
        var played = new DateTime(2024, 2, 2, 21, 15, 0, DateTimeKind.Utc);
        var lookedUp = new DateTime(2024, 3, 3, 8, 0, 0, DateTimeKind.Utc);
        var analyzed = new DateTime(2024, 4, 4, 7, 0, 0, DateTimeKind.Utc);

        MusicTrack track = TestTracks.Analyzed("user.wav", 145.0, tonic: 2, mode: KeyMode.Minor) with
        {
            Rating = 4,
            PlayCount = 7,
            DateAdded = added,
            LastPlayed = played,
            AnalysisIsManual = true,
            IntegratedLufs = -9.3,
            Structure = new SongStructure(
                new[] { new SongSection(0, "intro"), new SongSection(64.5, "drop") }, "test"),
            OnlineBpm = 72.5,
            OnlineBpmSource = "getsongbpm",
            BpmProvenance = BpmProvenance.LocalConfirmed,
            OnlineLookupUtc = lookedUp,
            LastAnalyzedUtc = analyzed,
            Kind = MusicMediaKind.Sample,
        };

        await store.SaveMusicAsync(new[] { track });
        MusicTrack loaded = (await store.LoadMusicAsync()).Single();

        Assert.Equal(4, loaded.Rating);
        Assert.Equal(7, loaded.PlayCount);
        Assert.Equal(added, loaded.DateAdded);
        Assert.Equal(played, loaded.LastPlayed);
        Assert.True(loaded.AnalysisIsManual);
        Assert.Equal(-9.3, loaded.IntegratedLufs!.Value, 6);
        Assert.Equal(new[] { "intro", "drop" }, loaded.Structure!.Ordered.Select(s => s.Label));
        Assert.Equal(64.5, loaded.Structure.Ordered[1].StartSeconds, 6);
        Assert.Equal(72.5, loaded.OnlineBpm!.Value, 6);
        Assert.Equal("getsongbpm", loaded.OnlineBpmSource);
        Assert.Equal(BpmProvenance.LocalConfirmed, loaded.BpmProvenance);
        Assert.Equal(lookedUp, loaded.OnlineLookupUtc);
        Assert.Equal(analyzed, loaded.LastAnalyzedUtc);
        Assert.Equal(MusicMediaKind.Sample, loaded.Kind);
    }

}
