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
    public async Task TwoStores_WithDistinctFileNames_PersistIndependentSets()
    {
        using var dir = new TempDirectory();
        var deckA = new JsonLiveSetStore(dir.Path);
        var deckB = new JsonLiveSetStore(dir.Path, fileName: "deck-b-set.json");

        await deckA.SaveAsync(new[] { "/m/a.wav" });
        await deckB.SaveAsync(new[] { "/m/b.wav" });

        Assert.NotEqual(deckA.Path, deckB.Path);
        Assert.Equal(new[] { "/m/a.wav" }, await deckA.LoadAsync());
        Assert.Equal(new[] { "/m/b.wav" }, await deckB.LoadAsync());
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

    [Fact]
    public async Task ConcurrentSaves_DoNotRaceOrThrow_AndLeaveAValidFile()
    {
        // Mirrors the autosave-on-every-queue-edit call site (ServiceConfig): rapid edits fire
        // overlapping SaveAsync calls. A fixed temp path + no gate races on the temp file; the
        // gated, unique-temp path must serialize cleanly and never leave a corrupt or temp file.
        using var dir = new TempDirectory();
        var store = new JsonLiveSetStore(dir.Path);

        var saves = Enumerable.Range(0, 40)
            .Select(i => store.SaveAsync(new[] { $"/m/{i}.wav" }))
            .ToArray();
        await Task.WhenAll(saves);

        IReadOnlyList<string>? loaded = await store.LoadAsync();
        Assert.NotNull(loaded);
        Assert.Single(loaded!);
        Assert.False(File.Exists(store.Path + ".tmp"));
        Assert.Empty(System.IO.Directory.GetFiles(
            System.IO.Path.GetDirectoryName(store.Path)!, "*.tmp"));
    }
}
