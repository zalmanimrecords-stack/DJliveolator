using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Xunit;

namespace Liveolator.Media.Tests;

public sealed class CatalogBackupTests
{
    private static readonly DateTime Noon = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreateVerified_WritesARestorableSingleFileCopy()
    {
        using var root = new TempDirectory();
        await SeedAsync(root.Path, 5);
        var backup = new CatalogBackup(root.Path);

        string copy = backup.CreateVerified(Noon)!;

        Assert.Equal(Path.Combine(backup.BackupDirectory, "catalog-20260929-120000.db"), copy);
        Assert.False(File.Exists(copy + "-wal"));
        Assert.Single(Directory.GetFiles(backup.BackupDirectory));
        using var restoreDir = new TempDirectory();
        File.Copy(copy, Path.Combine(restoreDir.Path, "catalog.db"));
        using var restored = new SqliteCatalogStore(restoreDir.Path);
        Assert.Equal(5, (await restored.LoadMusicAsync()).Count);
    }

    [Fact]
    public async Task CreateVerified_KeepsOnlyTheNewestCopies()
    {
        using var root = new TempDirectory();
        await SeedAsync(root.Path, 1);
        var backup = new CatalogBackup(root.Path, keep: 3);

        for (int i = 0; i < 5; i++)
            backup.CreateVerified(Noon.AddMinutes(i));

        string[] kept = Directory.GetFiles(backup.BackupDirectory).Select(Path.GetFileName).Order().ToArray()!;
        Assert.Equal(new[] { "catalog-20260929-120200.db", "catalog-20260929-120300.db", "catalog-20260929-120400.db" }, kept);
    }

    [Fact]
    public async Task CreateVerified_RefusesADamagedCatalog_AndKeepsTheOldBackups()
    {
        using var root = new TempDirectory();
        await SeedAsync(root.Path, 60);
        var backup = new CatalogBackup(root.Path);
        string good = backup.CreateVerified(Noon)!;
        Damage(Path.Combine(root.Path, "catalog.db"));

        Assert.Throws<CatalogCorruptException>(() => backup.CreateVerified(Noon.AddHours(1)));

        Assert.Equal(new[] { good }, Directory.GetFiles(backup.BackupDirectory));
    }

    [Fact]
    public void CreateVerified_ReturnsNull_WhenThereIsNoCatalogYet()
    {
        using var root = new TempDirectory();

        Assert.Null(new CatalogBackup(root.Path).CreateVerified(Noon));
    }

    private static async Task SeedAsync(string root, int count)
    {
        using var store = new SqliteCatalogStore(root);
        await store.SaveMusicAsync(Enumerable.Range(0, count).Select(i =>
            TestTracks.Analyzed($"track-{i:000}-{new string('x', 3000)}.flac", 120, 0, KeyMode.Major)));
    }

    private static void Damage(string db)
    {
        byte[] bytes = File.ReadAllBytes(db);
        for (int i = 2 * 4096; i < bytes.Length; i++)
            bytes[i] = (byte)(i * 31);
        File.WriteAllBytes(db, bytes);
    }
}
