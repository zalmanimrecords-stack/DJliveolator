using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library.Music;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Liveolator.Media.Tests;

/// <summary>
/// QA repro (2026-09-29, catalog.db corruption). Under MSIX AppData virtualization a Claude-launched
/// process opens the per-package catalog.db, but once that copy's own -wal/-shm were deleted (SQLite does
/// that on last close) the merged view shows the REAL App's live -wal/-shm beside it. SQLite cannot tell a
/// WAL belongs to another database, so it replays those frames into the fork. These tests pair a foreign
/// WAL with a catalog the same way, entirely inside temp directories. The mechanism is pinned so the
/// virtualization guard stays justified; the store's answer to it is pinned so the damage stays loud.
/// </summary>
public sealed class SqliteCatalogCorruptionReproTests
{
    // Mechanism proof: expected to PASS. It shows the pairing alone corrupts the catalog.
    [Fact]
    public async Task Mechanism_ForeignWalSidecar_CorruptsTheCatalog()
    {
        using var fork = new TempDirectory();
        using var real = new TempDirectory();
        // A raw write, because the store itself now refuses a paired file (next test).
        await PairForeignWalAsync(fork.Path, real.Path, () =>
        {
            using var connection = new SqliteConnection($"Data Source={Path.Combine(fork.Path, "catalog.db")};Pooling=False");
            connection.Open();
            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT OR REPLACE INTO folders(kind, path) VALUES('scan', 'x');";
            insert.ExecuteNonQuery(); // the write whose close checkpoints the foreign frames
            return Task.CompletedTask;
        });

        Assert.NotEqual("ok", IntegrityCheck(Path.Combine(fork.Path, "catalog.db")));
    }

    // The store's integrity check sees through the foreign WAL and refuses to write, so the incident's
    // write never happens through Liveolator and the damage is reported instead of served as a library.
    [Fact]
    public async Task ForeignWalPairing_IsRefused_InsteadOfWrittenInto()
    {
        using var fork = new TempDirectory();
        using var real = new TempDirectory();
        Exception? refused = null;
        await PairForeignWalAsync(fork.Path, real.Path, async () =>
        {
            using var store = new SqliteCatalogStore(fork.Path);
            refused = await Record.ExceptionAsync(() => store.SaveTrackAsync(Track("fork", 999)));
        });

        Assert.IsType<Liveolator.Core.Persistence.CatalogCorruptException>(refused);
    }

    // Builds the fork (40 checkpointed tracks), builds the real catalog (80 checkpointed + 20 live WAL
    // frames held open), exposes the real -wal/-shm beside the fork, then runs the fork write.
    private static async Task PairForeignWalAsync(string forkDir, string realDir, Func<Task> forkWrite)
    {
        using (var forkStore = new SqliteCatalogStore(forkDir))
            await forkStore.SaveMusicAsync(Enumerable.Range(0, 40).Select(i => Track("fork", i)));

        using (var realStore = new SqliteCatalogStore(realDir))
            await realStore.SaveMusicAsync(Enumerable.Range(0, 80).Select(i => Track("real", i)));

        string realDb = Path.Combine(realDir, "catalog.db");
        // The real App's open connection: keeps its WAL live (no checkpoint-on-close while it is open).
        using var holder = new SqliteConnection($"Data Source={realDb};Pooling=False");
        holder.Open();
        using (SqliteCommand pragma = holder.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; SELECT count(*) FROM tracks;";
            pragma.ExecuteScalar();
        }
        using (var realWriter = new SqliteCatalogStore(realDir))
            await realWriter.SaveMusicAsync(Enumerable.Range(80, 20).Select(i => Track("real", i)));

        Assert.False(File.Exists(Path.Combine(forkDir, "catalog.db-wal")), "fork must have no sidecar of its own");
        foreach (string suffix in new[] { "-wal", "-shm" })
            CopyShared(realDb + suffix, Path.Combine(forkDir, "catalog.db" + suffix));

        await forkWrite();
    }

    private static MusicTrack Track(string lineage, int i)
        // A long path forces overflow pages, like the real rows (beat grids, structure JSON).
        => TestTracks.Analyzed($"{lineage}-{i:000}-{new string('x', 3000)}.flac", 120 + i % 20, 0, KeyMode.Major);

    private static void CopyShared(string source, string destination)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = File.Create(destination);
        input.CopyTo(output);
    }

    private static string IntegrityCheck(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check(5);";
        try
        {
            using SqliteDataReader reader = command.ExecuteReader();
            var lines = new List<string>();
            while (reader.Read())
                lines.Add(reader.GetString(0));
            return string.Join("\n", lines);
        }
        catch (SqliteException ex)
        {
            return $"integrity_check threw: {ex.Message}";
        }
    }
}
