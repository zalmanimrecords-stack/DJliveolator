using Liveolator.Core.Persistence;
using Microsoft.Data.Sqlite;

namespace Liveolator.Media;

/// <summary>
/// Makes a verified, self-contained copy of a catalog database. The source is opened read-only and
/// copied with SQLite's online backup, never with a file copy: a file copy of a live WAL database is
/// torn, and a writable open of someone else's database (a server's, over the network) writes to it.
/// </summary>
public static class SqliteCatalogSnapshot
{
    /// <summary>
    /// Copies <paramref name="sourceDatabase"/> to <paramref name="destinationDatabase"/>, checks the copy's
    /// integrity and leaves it as a single file (no WAL). Throws <see cref="CatalogCorruptException"/>, and
    /// leaves no destination file, when the copy is not intact.
    /// </summary>
    /// <param name="sourceIsForeign">
    /// The source belongs to another process or machine (a server's catalog, possibly on a share). It is
    /// then opened immutable: no locks and no -wal/-shm beside it, so nothing is written there. That reads
    /// only checkpointed pages, and a copy caught mid-checkpoint fails the integrity check instead of
    /// tearing silently. The App's own catalog is copied normally, WAL included.
    /// </param>
    public static void Copy(string sourceDatabase, string destinationDatabase, bool sourceIsForeign = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDatabase);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDatabase);
        if (!File.Exists(sourceDatabase))
            throw new FileNotFoundException($"No catalog database at '{sourceDatabase}'.", sourceDatabase);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationDatabase))!);
        try
        {
            string sourceName = sourceIsForeign
                ? new Uri(Path.GetFullPath(sourceDatabase)).AbsoluteUri + "?immutable=1"
                : sourceDatabase;
            using (var source = Open(sourceName, SqliteOpenMode.ReadOnly))
            using (var destination = Open(destinationDatabase, SqliteOpenMode.ReadWriteCreate))
                source.BackupDatabase(destination);

            using var copy = Open(destinationDatabase, SqliteOpenMode.ReadWrite);
            using SqliteCommand command = copy.CreateCommand();
            command.CommandText = "PRAGMA integrity_check(1);";
            string verdict = command.ExecuteScalar() as string ?? "no answer";
            if (!string.Equals(verdict, "ok", StringComparison.Ordinal))
                throw new CatalogCorruptException(sourceDatabase, verdict);
            command.CommandText = "PRAGMA journal_mode=DELETE;";
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 11 or 26)
        {
            DeleteQuietly(destinationDatabase);
            throw new CatalogCorruptException(sourceDatabase, ex.Message, ex);
        }
        catch (CatalogCorruptException)
        {
            DeleteQuietly(destinationDatabase);
            throw;
        }
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
        }.ConnectionString);
        connection.Open();
        return connection;
    }

    private static void DeleteQuietly(string path)
    {
        foreach (string file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // A leftover half-copy is harmless: nothing loads it, and the next snapshot replaces it.
            }
        }
    }
}
