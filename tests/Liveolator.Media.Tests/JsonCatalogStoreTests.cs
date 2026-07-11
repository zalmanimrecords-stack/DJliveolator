using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Analysis.Structure;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;
using Xunit;

namespace Liveolator.Media.Tests;

public class JsonCatalogStoreTests
{
    [Fact]
    public async Task SaveTrack_UpsertsOneTrack_AndDeleteTrack_DropsIt()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);

        await store.SaveTrackAsync(TestTracks.Analyzed("a.wav", 120.0, 0, KeyMode.Major));
        await store.SaveTrackAsync(TestTracks.Analyzed("b.wav", 128.0, 0, KeyMode.Major));
        await store.SaveTrackAsync(TestTracks.Analyzed("a.wav", 140.0, 0, KeyMode.Major)); // upsert in place
        await store.DeleteTrackAsync("b.wav");

        MusicTrack only = Assert.Single(await store.LoadMusicAsync());
        Assert.Equal("a.wav", only.File.Path);
        Assert.Equal(140.0, only.Bpm!.Bpm);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsTracks()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);
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
        Assert.Equal(TrackAnalyzer.CurrentVersion, c.AnalyzerVersion);
        Assert.False(c.AnalysisIsManual);

        MusicTrack broken = loaded.Single(t => t.File.Path == "broken.mp3");
        Assert.Equal(MediaAnalysisStatus.Failed, broken.Status);
        Assert.Equal("decode error", broken.Error);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsBeatGridDownbeat()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);
        MusicTrack track = TestTracks.Analyzed("grid.wav", 128.0, tonic: 0, mode: KeyMode.Major) with
        {
            Bpm = new BpmResult(128.0, Confidence: 0.9, FirstBeatSeconds: 0.05)
            {
                DownbeatSeconds = 0.55,
                BeatsPerBar = 4,
                DownbeatConfidence = 0.62,
            },
        };

        await store.SaveMusicAsync(new[] { track });
        MusicTrack loaded = (await store.LoadMusicAsync()).Single();

        Assert.Equal(0.55, loaded.Bpm!.DownbeatSeconds, 6);
        Assert.Equal(4, loaded.Bpm.BeatsPerBar);
        Assert.Equal(0.62, loaded.Bpm.DownbeatConfidence, 6);
        Assert.Equal(0.05, loaded.Bpm.FirstBeatSeconds, 6); // existing anchor still intact
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsKind()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);
        var sample = TestTracks.Analyzed("loop.wav", 120.0, 0, KeyMode.Major, kind: MusicMediaKind.Sample);
        var song = TestTracks.Analyzed("song.wav", 128.0, 0, KeyMode.Major, kind: MusicMediaKind.Track);

        await store.SaveMusicAsync(new[] { sample, song });
        IReadOnlyList<MusicTrack> loaded = await store.LoadMusicAsync();

        Assert.Equal(MusicMediaKind.Sample, loaded.Single(t => t.File.Path == "loop.wav").Kind);
        Assert.Equal(MusicMediaKind.Track, loaded.Single(t => t.File.Path == "song.wav").Kind);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsSongStructure()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);
        var structure = new SongStructure(
            new[]
            {
                new SongSection(0.0, SongSectionLabel.Intro),
                new SongSection(32.0, SongSectionLabel.Drop),
                new SongSection(96.0, SongSectionLabel.Outro),
            },
            "librosa 0.10.2");
        MusicTrack track = TestTracks.Analyzed("s.wav", 128.0, tonic: 0, mode: KeyMode.Major) with
        {
            Structure = structure,
        };

        await store.SaveMusicAsync(new[] { track });
        MusicTrack loaded = (await store.LoadMusicAsync()).Single();

        // SongStructure.Sections is IReadOnlyList (reference equality), so compare element-wise.
        Assert.NotNull(loaded.Structure);
        Assert.Equal("librosa 0.10.2", loaded.Structure!.AnalyzedWith);
        Assert.Equal(structure.Sections, loaded.Structure.Sections);
    }

    [Fact]
    public async Task Load_CatalogWithoutStructureField_Succeeds_WithNullStructure()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);
        // A pre-structure catalog at the CURRENT version (3): the new Structure property is absent,
        // and must deserialize to null rather than fail the load (backward-compatible contract).
        const string json = """
            {
              "Version": 3,
              "Tracks": [
                {
                  "File": { "Path": "old.wav", "SizeBytes": 4096, "LastModifiedUtc": "2024-01-01T12:00:00Z" },
                  "Bpm": { "Bpm": 124.0, "Confidence": 0.9 },
                  "Key": null,
                  "Duration": "00:04:00",
                  "Cues": { "IntroStart": "00:00:02", "TrackEnd": "00:03:30" },
                  "Status": "Ok",
                  "Error": null,
                  "Kind": "Track",
                  "AnalyzerVersion": 1
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(store.MusicCatalogPath, json);

        IReadOnlyList<MusicTrack> loaded = await store.LoadMusicAsync();

        MusicTrack track = Assert.Single(loaded);
        Assert.Equal("old.wav", track.File.Path);
        Assert.Null(track.Structure);
    }

    [Fact]
    public async Task SampleFolders_RoundTrip()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);

        await store.SaveSampleFoldersAsync(new[] { "/m/loops", "/m/oneshots" });
        IReadOnlyList<string> loaded = await store.LoadSampleFoldersAsync();

        Assert.Equal(new[] { "/m/loops", "/m/oneshots" }, loaded);
    }

    [Fact]
    public async Task LoadSampleFolders_WhenNone_ReturnsEmpty()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);
        Assert.Empty(await store.LoadSampleFoldersAsync());
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsTrackMetadata()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);
        var meta = new TrackMetadata("Midnight City", "M83", "Hurry Up", "M83", "Electronic",
            2011, 3, "demo", 320, 44100, 2, "MP3");
        var track = TestTracks.Analyzed("m.wav", 128.0, tonic: 0, mode: KeyMode.Major, metadata: meta);

        await store.SaveMusicAsync(new[] { track });
        IReadOnlyList<MusicTrack> loaded = await store.LoadMusicAsync();

        Assert.Equal(meta, loaded.Single().Metadata);
    }

    [Fact]
    public async Task Load_OlderSchemaVersion_ReturnsEmpty_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonCatalogStore(dir.Path, onWarning: w => warning = w);
        // A pre-metadata (v1) snapshot must be discarded, not served with empty tags.
        await File.WriteAllTextAsync(store.MusicCatalogPath, "{\"Version\":1,\"Tracks\":[]}");

        IReadOnlyList<MusicTrack> loaded = await store.LoadMusicAsync();

        Assert.Empty(loaded);
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsVisualAssets()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);
        var assets = new[]
        {
            TestTracks.Video("clip.mp4", 1920, 1080, 12.5),
            TestTracks.Image("logo.png", 800, 600),
        };

        await store.SaveVisualAsync(assets);
        IReadOnlyList<VisualAsset> loaded = await store.LoadVisualAsync();

        Assert.Equal(2, loaded.Count);
        VisualAsset clip = loaded.Single(a => a.File.Path == "clip.mp4");
        Assert.Equal(VisualMediaKind.Video, clip.Kind);
        Assert.Equal(1920, clip.Info!.Value.Width);
        Assert.Equal(TimeSpan.FromSeconds(12.5), clip.Info.Value.Duration);

        VisualAsset image = loaded.Single(a => a.File.Path == "logo.png");
        Assert.Equal(VisualMediaKind.Image, image.Kind);
        Assert.Null(image.Info!.Value.Duration);
    }

    [Fact]
    public async Task MusicAndVisualCatalogs_ArePersistedSeparately()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);

        await store.SaveMusicAsync(new[] { TestTracks.Analyzed("a.wav", 120, 0, KeyMode.Major) });
        await store.SaveVisualAsync(new[] { TestTracks.Image("logo.png", 800, 600) });

        Assert.NotEqual(store.MusicCatalogPath, store.VisualCatalogPath);
        Assert.Single(await store.LoadMusicAsync());
        Assert.Single(await store.LoadVisualAsync());
    }

    [Fact]
    public async Task Load_WhenNoCacheExists_ReturnsEmpty()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);

        IReadOnlyList<MusicTrack> loaded = await store.LoadMusicAsync();

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task Load_CorruptCache_ReturnsEmpty_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonCatalogStore(dir.Path, onWarning: w => warning = w);
        await File.WriteAllTextAsync(store.MusicCatalogPath, "{ this is not valid json");

        IReadOnlyList<MusicTrack> loaded = await store.LoadMusicAsync();

        Assert.Empty(loaded);
        Assert.NotNull(warning); // never silently swallowed
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsScanFolders()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);
        var folders = new[] { @"C:\Music\House", @"C:\Music\Techno" };

        await store.SaveScanFoldersAsync(folders);
        IReadOnlyList<string> loaded = await store.LoadScanFoldersAsync();

        Assert.Equal(folders, loaded);
    }

    [Fact]
    public async Task LoadScanFolders_WhenNoneExist_ReturnsEmpty()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);

        IReadOnlyList<string> loaded = await store.LoadScanFoldersAsync();

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task LoadScanFolders_CorruptFile_ReturnsEmpty_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonCatalogStore(dir.Path, onWarning: w => warning = w);
        await File.WriteAllTextAsync(store.ScanFoldersPath, "{ not valid json");

        IReadOnlyList<string> loaded = await store.LoadScanFoldersAsync();

        Assert.Empty(loaded);
        Assert.NotNull(warning); // a bad file never silently loses the user's folders
    }

    [Fact]
    public async Task ScanFolders_ArePersistedSeparatelyFromTheCatalog()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);

        await store.SaveMusicAsync(new[] { TestTracks.Analyzed("a.wav", 120, 0, KeyMode.Major) });
        await store.SaveScanFoldersAsync(new[] { @"C:\Music" });

        Assert.NotEqual(store.MusicCatalogPath, store.ScanFoldersPath);
        Assert.Single(await store.LoadMusicAsync());
        Assert.Single(await store.LoadScanFoldersAsync());
    }

    [Fact]
    public async Task Save_IsAtomic_NoLeftoverTempFile()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);

        await store.SaveMusicAsync(new[] { TestTracks.Analyzed("a.wav", 120, 0, KeyMode.Major) });

        Assert.True(File.Exists(store.MusicCatalogPath));
        Assert.False(File.Exists(store.MusicCatalogPath + ".tmp"));
    }

    [Fact]
    public async Task ConcurrentSaves_AreSerialized_AndLeaveAValidCatalog()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);
        MusicTrack[][] snapshots = Enumerable.Range(0, 24)
            .Select(save => Enumerable.Range(0, 200)
                .Select(track => TestTracks.Analyzed(
                    $"save-{save:D2}-track-{track:D3}.wav",
                    120 + save,
                    0,
                    KeyMode.Major))
                .ToArray())
            .ToArray();

        await Task.WhenAll(snapshots.Select(snapshot => store.SaveMusicAsync(snapshot)));

        IReadOnlyList<MusicTrack> loaded = await store.LoadMusicAsync();
        Assert.Equal(200, loaded.Count);
        Assert.Single(loaded.Select(track => track.File.Path[..7]).Distinct());
        Assert.Empty(Directory.EnumerateFiles(dir.Path, "*.tmp"));
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsVisualScanFolders()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);
        var folders = new[] { @"C:\Visuals\Loops", @"C:\Visuals\Stills" };

        await store.SaveVisualScanFoldersAsync(folders);
        IReadOnlyList<string> loaded = await store.LoadVisualScanFoldersAsync();

        Assert.Equal(folders, loaded);
    }

    [Fact]
    public async Task LoadVisualScanFolders_WhenNoneExist_ReturnsEmpty()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);

        Assert.Empty(await store.LoadVisualScanFoldersAsync());
    }

    [Fact]
    public async Task LoadVisualScanFolders_CorruptFile_ReturnsEmpty_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonCatalogStore(dir.Path, onWarning: w => warning = w);
        await File.WriteAllTextAsync(store.VisualScanFoldersPath, "{ not valid json");

        IReadOnlyList<string> loaded = await store.LoadVisualScanFoldersAsync();

        Assert.Empty(loaded);
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task VisualAndMusicScanFolders_ArePersistedSeparately()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);

        await store.SaveScanFoldersAsync(new[] { @"C:\Music" });
        await store.SaveVisualScanFoldersAsync(new[] { @"C:\Visuals\A", @"C:\Visuals\B" });

        Assert.NotEqual(store.ScanFoldersPath, store.VisualScanFoldersPath);
        Assert.Single(await store.LoadScanFoldersAsync());
        Assert.Equal(2, (await store.LoadVisualScanFoldersAsync()).Count);
    }
}
