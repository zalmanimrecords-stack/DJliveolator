using Xunit;

namespace Liveolator.Media.Tests;

public class JsonLiveSetStoreTests
{
    [Fact]
    public async Task SaveThenLoad_RoundTripsOrderedTracks()
    {
        using var dir = new TempDirectory();
        var store = new JsonLiveSetStore(dir.Path);

        await store.SaveAsync(new[] { "/m/a.wav", "/m/b.wav", "/m/c.wav" });
        IReadOnlyList<string>? loaded = await store.LoadAsync();

        Assert.Equal(new[] { "/m/a.wav", "/m/b.wav", "/m/c.wav" }, loaded);
    }

    [Fact]
    public async Task Load_Missing_ReturnsNull()
    {
        using var dir = new TempDirectory();
        var store = new JsonLiveSetStore(dir.Path);

        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task Save_EmptyList_RoundTripsAsEmpty()
    {
        using var dir = new TempDirectory();
        var store = new JsonLiveSetStore(dir.Path);

        await store.SaveAsync(Array.Empty<string>());
        IReadOnlyList<string>? loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Empty(loaded!);
    }

    [Fact]
    public async Task Load_CorruptFile_ReturnsNull_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonLiveSetStore(dir.Path, onWarning: w => warning = w);
        await store.SaveAsync(new[] { "/m/a.wav" });
        await File.WriteAllTextAsync(store.Path, "{ not valid json");

        Assert.Null(await store.LoadAsync());
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task Load_OlderVersion_ReturnsNull_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonLiveSetStore(dir.Path, onWarning: w => warning = w);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(store.Path)!);
        await File.WriteAllTextAsync(store.Path, "{\"Version\":0,\"TrackPaths\":[]}");

        Assert.Null(await store.LoadAsync());
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task Save_IsAtomic_NoLeftoverTempFile()
    {
        using var dir = new TempDirectory();
        var store = new JsonLiveSetStore(dir.Path);

        await store.SaveAsync(new[] { "/m/a.wav" });

        Assert.True(File.Exists(store.Path));
        Assert.False(File.Exists(store.Path + ".tmp"));
    }
}
