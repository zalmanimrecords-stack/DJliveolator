using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Xunit;

namespace Liveolator.Media.Tests;

public class JsonCatalogStoreTests
{
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

        MusicTrack broken = loaded.Single(t => t.File.Path == "broken.mp3");
        Assert.Equal(MediaAnalysisStatus.Failed, broken.Status);
        Assert.Equal("decode error", broken.Error);
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
    public async Task Save_IsAtomic_NoLeftoverTempFile()
    {
        using var dir = new TempDirectory();
        var store = new JsonCatalogStore(dir.Path);

        await store.SaveMusicAsync(new[] { TestTracks.Analyzed("a.wav", 120, 0, KeyMode.Major) });

        Assert.True(File.Exists(store.MusicCatalogPath));
        Assert.False(File.Exists(store.MusicCatalogPath + ".tmp"));
    }
}
