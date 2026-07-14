using Liveolator.Core.Playlist;
using Xunit;

namespace Liveolator.Media.Tests;

public class JsonPlaylistStoreTests
{
    private static string FileFor(JsonPlaylistStore store, string cleanName)
        => Path.Combine(store.Directory, cleanName + ".json");

    [Fact]
    public async Task SaveThenLoad_RoundTripsNameAndOrderedTracks()
    {
        using var dir = new TempDirectory();
        var store = new JsonPlaylistStore(dir.Path);
        var playlist = new Playlist("Warmup", new[] { "/m/a.wav", "/m/b.wav", "/m/c.wav" });

        await store.SaveAsync(playlist);
        Playlist? loaded = await store.LoadAsync("Warmup");

        Assert.NotNull(loaded);
        Assert.Equal("Warmup", loaded!.Name);
        Assert.Equal(new[] { "/m/a.wav", "/m/b.wav", "/m/c.wav" }, loaded.TrackPaths);
    }

    [Fact]
    public async Task List_ReturnsSavedNames_Sorted()
    {
        using var dir = new TempDirectory();
        var store = new JsonPlaylistStore(dir.Path);
        await store.SaveAsync(new Playlist("Peak", new[] { "/m/a.wav" }));
        await store.SaveAsync(new Playlist("Closing", new[] { "/m/b.wav" }));

        IReadOnlyList<string> names = await store.ListAsync();

        Assert.Equal(new[] { "Closing", "Peak" }, names);
    }

    [Fact]
    public async Task Delete_RemovesPlaylist()
    {
        using var dir = new TempDirectory();
        var store = new JsonPlaylistStore(dir.Path);
        await store.SaveAsync(new Playlist("Temp", new[] { "/m/a.wav" }));

        await store.DeleteAsync("Temp");

        Assert.Null(await store.LoadAsync("Temp"));
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task Load_Missing_ReturnsNull()
    {
        using var dir = new TempDirectory();
        var store = new JsonPlaylistStore(dir.Path);

        Assert.Null(await store.LoadAsync("nope"));
    }

    [Fact]
    public async Task Load_CorruptFile_ReturnsNull_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonPlaylistStore(dir.Path, onWarning: w => warning = w);
        await store.SaveAsync(new Playlist("Broken", new[] { "/m/a.wav" }));
        await File.WriteAllTextAsync(FileFor(store, "Broken"), "{ not valid json");

        Assert.Null(await store.LoadAsync("Broken"));
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task Load_OlderVersion_ReturnsNull_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonPlaylistStore(dir.Path, onWarning: w => warning = w);
        System.IO.Directory.CreateDirectory(store.Directory);
        await File.WriteAllTextAsync(FileFor(store, "Old"),
            "{\"Version\":0,\"Name\":\"Old\",\"TrackPaths\":[]}");

        Assert.Null(await store.LoadAsync("Old"));
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task Save_IsAtomic_NoLeftoverTempFile()
    {
        using var dir = new TempDirectory();
        var store = new JsonPlaylistStore(dir.Path);

        await store.SaveAsync(new Playlist("Set", new[] { "/m/a.wav" }));

        Assert.True(File.Exists(FileFor(store, "Set")));
        Assert.False(File.Exists(FileFor(store, "Set") + ".tmp"));
    }

    [Fact]
    public async Task Save_NameWithIllegalChars_RoundTripsDisplayName()
    {
        using var dir = new TempDirectory();
        var store = new JsonPlaylistStore(dir.Path);
        var playlist = new Playlist("Friday: 90s/2000s", new[] { "/m/a.wav" });

        await store.SaveAsync(playlist);
        Playlist? loaded = await store.LoadAsync("Friday: 90s/2000s");

        Assert.NotNull(loaded);
        Assert.Equal("Friday: 90s/2000s", loaded!.Name); // display name preserved despite sanitized filename
    }
}
