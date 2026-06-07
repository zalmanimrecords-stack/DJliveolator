using System.Text.Json;
using System.Text.Json.Serialization;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;
using Liveolator.Core.Persistence;
using Microsoft.Data.Sqlite;

namespace Liveolator.Media;

/// <summary>
/// The single SQLite-backed gateway for the persisted library metadata (doc 13). It is the ONE class
/// through which all database access flows — every table, query and transaction lives here, so no other
/// type opens a connection or speaks SQL. It implements the same Core seams as
/// <see cref="JsonCatalogStore"/> (<see cref="IMusicCatalogStore"/> + <see cref="IVisualCatalogStore"/>),
/// making it a drop-in replacement that stores the whole analyzed catalog, the visual assets and the
/// scan/sample folder roots in a single database file.
/// </summary>
/// <remarks>
/// <para>
/// Each entity is one row: a primary-key <c>path</c>, a few promoted, indexable columns for fast
/// querying (status / BPM / Camelot key / kind), and a <c>payload</c> column holding the full domain
/// record as JSON. Storing the record as JSON keeps the schema resilient to model changes — a new field
/// on <see cref="MusicTrack"/> needs no migration — while still persisting every piece of metadata.
/// </para>
/// <para>
/// Loads are tolerant: a missing, locked or non-database file yields an empty result and a warning, never
/// an exception (global standards #16/#26). Saves run in a transaction and replace the whole set, so an
/// interrupted write rolls back and never leaves the catalog half-updated or with stale rows.
/// </para>
/// </remarks>
public sealed class SqliteCatalogStore : IMusicCatalogStore, IVisualCatalogStore
{
    /// <summary>Current schema version, recorded in <c>schema_meta</c> for future safe migrations (#22).</summary>
    private const int SchemaVersion = 1;

    // Folder-list scopes (one table, scoped rows) — mirrors the three JSON folder files.
    private const string ScanScope = "scan";
    private const string SampleScope = "sample";
    private const string VisualScanScope = "visual_scan";

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _connectionString;
    private readonly Action<string>? _onWarning;

    /// <param name="databasePath">
    /// Full path to the database file; defaults to <c>%APPDATA%/Liveolator/catalog.db</c> (or the
    /// platform equivalent). The parent directory is created on first write.
    /// </param>
    /// <param name="onWarning">Receives a human-readable note when a load degrades; never throws.</param>
    public SqliteCatalogStore(string? databasePath = null, Action<string>? onWarning = null)
    {
        DatabasePath = databasePath ?? DefaultDatabasePath();
        // Pooling=False so the file handle is released as soon as a connection closes — the file can then
        // be moved/deleted by the OS (and by tests) without a lingering lock from the connection pool.
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Pooling = false,
        }.ToString();
        _onWarning = onWarning;
    }

    /// <summary>Full path of the SQLite database file backing this store.</summary>
    public string DatabasePath { get; }

    /// <summary>Default persistence file: <c>%APPDATA%/Liveolator/catalog.db</c> (or the XDG/Mac equivalent).</summary>
    public static string DefaultDatabasePath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Liveolator", "catalog.db");

    // --- Music catalog (IMusicCatalogStore) ---

    public Task SaveMusicAsync(IEnumerable<MusicTrack> tracks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        IReadOnlyList<MusicTrack> list = tracks.ToList();
        return ReplaceAllAsync("music_tracks", list, InsertMusic, cancellationToken);
    }

    public Task<IReadOnlyList<MusicTrack>> LoadMusicAsync(CancellationToken cancellationToken = default)
        => LoadPayloadsAsync<MusicTrack>("music_tracks", cancellationToken);

    public Task SaveScanFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
        => SaveFoldersAsync(ScanScope, folders, cancellationToken);

    public Task<IReadOnlyList<string>> LoadScanFoldersAsync(CancellationToken cancellationToken = default)
        => LoadFoldersAsync(ScanScope, cancellationToken);

    public Task SaveSampleFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
        => SaveFoldersAsync(SampleScope, folders, cancellationToken);

    public Task<IReadOnlyList<string>> LoadSampleFoldersAsync(CancellationToken cancellationToken = default)
        => LoadFoldersAsync(SampleScope, cancellationToken);

    // --- Visual catalog (IVisualCatalogStore) ---

    public Task SaveVisualAsync(IEnumerable<VisualAsset> assets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assets);
        IReadOnlyList<VisualAsset> list = assets.ToList();
        return ReplaceAllAsync("visual_assets", list, InsertVisual, cancellationToken);
    }

    public Task<IReadOnlyList<VisualAsset>> LoadVisualAsync(CancellationToken cancellationToken = default)
        => LoadPayloadsAsync<VisualAsset>("visual_assets", cancellationToken);

    public Task SaveVisualScanFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
        => SaveFoldersAsync(VisualScanScope, folders, cancellationToken);

    public Task<IReadOnlyList<string>> LoadVisualScanFoldersAsync(CancellationToken cancellationToken = default)
        => LoadFoldersAsync(VisualScanScope, cancellationToken);

    // --- Row writers (promoted query columns + full JSON payload) ---

    private static void InsertMusic(SqliteCommand cmd, MusicTrack track)
    {
        cmd.CommandText =
            "INSERT INTO music_tracks (path, status, bpm, camelot, kind, payload) " +
            "VALUES ($path, $status, $bpm, $camelot, $kind, $payload)";
        cmd.Parameters.AddWithValue("$path", track.File.Path);
        cmd.Parameters.AddWithValue("$status", track.Status.ToString());
        cmd.Parameters.AddWithValue("$bpm", (object?)track.Bpm?.Bpm ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$camelot", (object?)track.Key?.Camelot ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$kind", track.Kind.ToString());
        cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(track, PayloadOptions));
    }

    private static void InsertVisual(SqliteCommand cmd, VisualAsset asset)
    {
        cmd.CommandText =
            "INSERT INTO visual_assets (path, kind, status, payload) " +
            "VALUES ($path, $kind, $status, $payload)";
        cmd.Parameters.AddWithValue("$path", asset.File.Path);
        cmd.Parameters.AddWithValue("$kind", asset.Kind.ToString());
        cmd.Parameters.AddWithValue("$status", asset.Status.ToString());
        cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(asset, PayloadOptions));
    }

    // --- Shared SQL plumbing (the only place that opens a connection / runs SQL) ---

    // Transactionally clear the table and re-insert the whole set, so a save is atomic and never leaves
    // stale rows. A write failure surfaces to the caller (the App logs it) after the transaction rolls back.
    private async Task ReplaceAllAsync<T>(
        string table, IReadOnlyList<T> rows, Action<SqliteCommand, T> bind, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (SqliteCommand clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = $"DELETE FROM {table}";
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (T row in rows)
        {
            await using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            bind(insert, row);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<T>> LoadPayloadsAsync<T>(string table, CancellationToken cancellationToken)
    {
        try
        {
            await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT payload FROM {table}";

            var items = new List<T>();
            await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string payload = reader.GetString(0);
                T? item = JsonSerializer.Deserialize<T>(payload, PayloadOptions);
                if (item is not null)
                    items.Add(item);
            }

            return items;
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or IOException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke($"Catalog database '{DatabasePath}' could not be read ({ex.Message}); starting empty.");
            return Array.Empty<T>();
        }
    }

    private async Task SaveFoldersAsync(string scope, IEnumerable<string> folders, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        IReadOnlyList<string> list = folders.ToList();

        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (SqliteCommand clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM folder_roots WHERE scope = $scope";
            clear.Parameters.AddWithValue("$scope", scope);
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        for (int ordinal = 0; ordinal < list.Count; ordinal++)
        {
            await using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                "INSERT INTO folder_roots (scope, ord, path) VALUES ($scope, $ord, $path)";
            insert.Parameters.AddWithValue("$scope", scope);
            insert.Parameters.AddWithValue("$ord", ordinal);
            insert.Parameters.AddWithValue("$path", list[ordinal]);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> LoadFoldersAsync(string scope, CancellationToken cancellationToken)
    {
        try
        {
            await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT path FROM folder_roots WHERE scope = $scope ORDER BY ord";
            cmd.Parameters.AddWithValue("$scope", scope);

            var folders = new List<string>();
            await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                folders.Add(reader.GetString(0));

            return folders;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke($"Catalog database '{DatabasePath}' could not be read ({ex.Message}); starting empty.");
            return Array.Empty<string>();
        }
    }

    // Opens a connection and guarantees the schema exists. The directory is created on demand so the very
    // first save succeeds on a clean machine. EnsureSchema is idempotent (CREATE ... IF NOT EXISTS).
    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS schema_meta (version INTEGER NOT NULL);
            INSERT INTO schema_meta (version)
                SELECT $version WHERE NOT EXISTS (SELECT 1 FROM schema_meta);

            CREATE TABLE IF NOT EXISTS music_tracks (
                path    TEXT PRIMARY KEY,
                status  TEXT,
                bpm     REAL,
                camelot TEXT,
                kind    TEXT,
                payload TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_music_bpm  ON music_tracks (bpm);
            CREATE INDEX IF NOT EXISTS ix_music_kind ON music_tracks (kind);

            CREATE TABLE IF NOT EXISTS visual_assets (
                path    TEXT PRIMARY KEY,
                kind    TEXT,
                status  TEXT,
                payload TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS folder_roots (
                scope TEXT NOT NULL,
                ord   INTEGER NOT NULL,
                path  TEXT NOT NULL,
                PRIMARY KEY (scope, path)
            );
            """;
        cmd.Parameters.AddWithValue("$version", SchemaVersion);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
