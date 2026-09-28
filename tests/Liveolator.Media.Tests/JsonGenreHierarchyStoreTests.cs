using Liveolator.Core.Library.Music;
using Xunit;

namespace Liveolator.Media.Tests;

public class JsonGenreHierarchyStoreTests
{
    [Fact]
    public async Task SaveThenLoad_WithFreshInstance_RoundTripsTheTree()
    {
        using var dir = new TempDirectory();
        var hierarchy = new GenreHierarchy()
            .SetParent("Deep House", "House")
            .SetParent("Tech House", "House");

        await new JsonGenreHierarchyStore(dir.Path).SaveAsync(hierarchy);

        // Fresh store instance, so this reads the file, not any in-memory state.
        GenreHierarchy reloaded = await new JsonGenreHierarchyStore(dir.Path).LoadAsync();

        Assert.Equal("House", reloaded.ParentOf("Deep House"));
        Assert.Equal("House", reloaded.ParentOf("Tech House"));
        Assert.Equal(
            new[] { "Deep House", "Tech House" },
            reloaded.ChildrenOf("House").OrderBy(g => g, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsEmptyTree_NoException()
    {
        using var dir = new TempDirectory();
        var store = new JsonGenreHierarchyStore(dir.Path);

        GenreHierarchy loaded = await store.LoadAsync();

        Assert.Null(loaded.ParentOf("House"));
        Assert.Empty(loaded.Mappings);
    }

    [Fact]
    public async Task Load_CorruptFile_ReturnsEmptyTree_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonGenreHierarchyStore(dir.Path, onWarning: w => warning = w);
        await File.WriteAllTextAsync(store.GenreHierarchyPath, "{ not valid json");

        GenreHierarchy loaded = await store.LoadAsync();

        Assert.Empty(loaded.Mappings);
        Assert.NotNull(warning); // never silently swallowed
    }

    [Fact]
    public async Task Load_IncompatibleVersion_ReturnsEmptyTree_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonGenreHierarchyStore(dir.Path, onWarning: w => warning = w);
        await File.WriteAllTextAsync(store.GenreHierarchyPath, "{\"Version\":999,\"ParentByGenre\":{}}");

        GenreHierarchy loaded = await store.LoadAsync();

        Assert.Empty(loaded.Mappings);
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task Save_IsAtomic_NoLeftoverTempFile()
    {
        using var dir = new TempDirectory();
        var store = new JsonGenreHierarchyStore(dir.Path);

        await store.SaveAsync(new GenreHierarchy().SetParent("Deep House", "House"));

        Assert.True(File.Exists(store.GenreHierarchyPath));
        Assert.False(File.Exists(store.GenreHierarchyPath + ".tmp"));
    }

    [Fact]
    public async Task GenreHierarchyStore_IsSeparateFile_FromMusicCatalogAndCues()
    {
        // Backward-compatibility guard, same shape as the cue store's equivalent test: the hierarchy
        // lives in its own file, so saving it never touches catalog.music.json or catalog.cues.json.
        using var dir = new TempDirectory();
        var catalog = new JsonCatalogStore(dir.Path);
        var cues = new JsonHotCueStore(dir.Path);
        var genres = new JsonGenreHierarchyStore(dir.Path);

        var track = TestTracks.Analyzed("a.wav", 120, 0, Liveolator.Core.Analysis.Key.KeyMode.Major);
        await catalog.SaveMusicAsync(new[] { track });
        await genres.SaveAsync(new GenreHierarchy().SetParent("Deep House", "House"));

        Assert.NotEqual(catalog.MusicCatalogPath, genres.GenreHierarchyPath);
        Assert.NotEqual(cues.CuesPath, genres.GenreHierarchyPath);

        Assert.Single(await catalog.LoadMusicAsync()); // catalog still valid and unchanged
        Assert.Empty((await cues.ListPathsWithCuesAsync())); // cue store untouched
        Assert.Equal("House", (await genres.LoadAsync()).ParentOf("Deep House"));
    }
}
