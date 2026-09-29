using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Xunit;

namespace Liveolator.Media.Tests;

/// <summary>
/// A damaged catalog must fail loudly and stay untouched. Serving it as an empty library is what lets
/// the App offer a re-scan that writes into the broken file.
/// </summary>
public sealed class SqliteCatalogStoreCorruptionTests
{
    [Fact]
    public async Task Load_ThrowsCatalogCorrupt_ForADamagedDatabase()
    {
        using var dir = new TempDirectory();
        await SeedAndDamageAsync(dir.Path);
        using var store = new SqliteCatalogStore(dir.Path);

        CatalogCorruptException error = await Assert.ThrowsAsync<CatalogCorruptException>(() => store.LoadMusicAsync());

        Assert.Equal(Path.Combine(dir.Path, "catalog.db"), error.CatalogPath);
    }

    [Fact]
    public async Task Writes_AreRefused_AndLeaveTheDamagedFileByteForByte()
    {
        using var dir = new TempDirectory();
        await SeedAndDamageAsync(dir.Path);
        string db = Path.Combine(dir.Path, "catalog.db");
        byte[] before = await File.ReadAllBytesAsync(db);
        using var store = new SqliteCatalogStore(dir.Path);

        await Assert.ThrowsAsync<CatalogCorruptException>(() => store.SaveTrackAsync(Track(999)));
        await Assert.ThrowsAsync<CatalogCorruptException>(() => store.SaveScanFoldersAsync(new[] { "M:\\" }));
        await Assert.ThrowsAsync<CatalogCorruptException>(() => store.LoadScanFoldersAsync());

        Assert.Equal(before, await File.ReadAllBytesAsync(db));
    }

    [Fact]
    public async Task AHealthyCatalog_StillLoads()
    {
        using var dir = new TempDirectory();
        using (var seed = new SqliteCatalogStore(dir.Path))
            await seed.SaveMusicAsync(Enumerable.Range(0, 3).Select(Track));
        using var store = new SqliteCatalogStore(dir.Path);

        Assert.Equal(3, (await store.LoadMusicAsync()).Count);
    }

    private static async Task SeedAndDamageAsync(string directory)
    {
        using (var seed = new SqliteCatalogStore(directory))
            await seed.SaveMusicAsync(Enumerable.Range(0, 60).Select(Track));

        // Scribble over every page after the schema page, the shape of the real incident: the header and
        // schema survive, so the file still opens, but the tracks b-tree is garbage.
        string db = Path.Combine(directory, "catalog.db");
        byte[] bytes = await File.ReadAllBytesAsync(db);
        const int pageSize = 4096;
        for (int i = 2 * pageSize; i < bytes.Length; i++)
            bytes[i] = (byte)(i * 31);
        await File.WriteAllBytesAsync(db, bytes);
    }

    private static MusicTrack Track(int i)
        => TestTracks.Analyzed($"track-{i:000}-{new string('x', 3000)}.flac", 120 + i % 20, 0, KeyMode.Major);
}
