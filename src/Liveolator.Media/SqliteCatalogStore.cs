using System.Text.Json;
using System.Text.Json.Serialization;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;
using Liveolator.Core.Persistence;
using Microsoft.Data.Sqlite;

namespace Liveolator.Media;

/// <summary>
/// Persists the analyzed catalog in a single SQLite database (WAL mode) under the per-user app-data
/// root. Replaces the whole-file JSON rewrite (<see cref="JsonCatalogStore"/>): tracks are upserted
/// per-row by path inside a transaction, so a save is O(changed) not O(catalog), and — crucially —
/// the App and the MCP server can share one catalog without last-writer-wins clobbering each other's
/// rows (doc 31 M1). WAL gives concurrent readers + a serialized cross-process writer; a busy_timeout
/// makes a second writer wait rather than fail.
/// </summary>
/// <remarks>
/// Per the doc 31 design, <see cref="SaveMusicAsync"/> is <b>upsert-only</b>: it never deletes rows
/// missing from the given set, so one process can't drop a track another just added. Removal goes
/// through the explicit <see cref="DeleteTrackAsync"/> path. Each track is stored as a JSON blob (the
/// same serialization the JSON store used), tagged with the catalog schema version so a version bump
/// retires old rows and triggers a clean re-scan, exactly like the JSON store did. Loads are tolerant:
/// an unreadable DB or row yields an empty/skipped result with a warning, never a crash (global #16/#26).
/// </remarks>
public sealed class SqliteCatalogStore : IMusicCatalogStore, IVisualCatalogStore, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Folder-list kinds stored in the shared folders table.
    private const string ScanKind = "scan";
    private const string VisualScanKind = "visual-scan";
    private const string SampleKind = "sample";

    private readonly string _dbPath;
    private readonly Action<string>? _onWarning;
    // Serializes writes within this process; SQLite's WAL + busy_timeout serialize across processes.
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialized;

    public SqliteCatalogStore(string? rootDirectory = null, Action<string>? onWarning = null)
    {
        string directory = rootDirectory ?? JsonCatalogStore.DefaultRoot();
        Directory.CreateDirectory(directory);
        _dbPath = Path.Combine(directory, "catalog.db");
        _onWarning = onWarning;
    }

    /// <summary>Full path of the SQLite catalog database file.</summary>
    public string DatabasePath => _dbPath;

    private const string UpsertTrackSql =
        @"INSERT INTO tracks(path, schema_version, data) VALUES($path, $ver, $data)
          ON CONFLICT(path) DO UPDATE SET schema_version = excluded.schema_version, data = excluded.data;";

    public async Task SaveMusicAsync(IEnumerable<MusicTrack> tracks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        List<MusicTrack> list = tracks.ToList();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = UpsertTrackSql;
            SqliteParameter path = command.Parameters.Add("$path", SqliteType.Text);
            SqliteParameter ver = command.Parameters.Add("$ver", SqliteType.Integer);
            SqliteParameter data = command.Parameters.Add("$data", SqliteType.Text);
            foreach (MusicTrack track in list)
            {
                path.Value = track.File.Path;
                ver.Value = MusicCatalogSnapshot.CurrentVersion;
                data.Value = JsonSerializer.Serialize(track, SerializerOptions);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            transaction.Commit();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// Upserts a single track in its own transaction — the incremental-scan write (one cheap row write,
    /// not an O(catalog) rewrite), so each scanned track is durable the instant it is analyzed.
    /// </summary>
    public async Task SaveTrackAsync(MusicTrack track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = UpsertTrackSql;
            command.Parameters.AddWithValue("$path", track.File.Path);
            command.Parameters.AddWithValue("$ver", MusicCatalogSnapshot.CurrentVersion);
            command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(track, SerializerOptions));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<MusicTrack>> LoadMusicAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<MusicTrack>();
        try
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            // Only current-schema rows: a version bump retires old blobs so they re-scan, mirroring the
            // JSON store's version-mismatch policy (no miscategorized stale data is served).
            command.CommandText = "SELECT data FROM tracks WHERE schema_version = $ver;";
            command.Parameters.AddWithValue("$ver", MusicCatalogSnapshot.CurrentVersion);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string json = reader.GetString(0);
                try
                {
                    MusicTrack? track = JsonSerializer.Deserialize<MusicTrack>(json, SerializerOptions);
                    if (track is not null)
                        result.Add(track);
                }
                catch (JsonException ex)
                {
                    _onWarning?.Invoke($"Skipping an unreadable catalog row in '{_dbPath}' ({ex.Message}).");
                }
            }
        }
        catch (SqliteException ex)
        {
            _onWarning?.Invoke($"Catalog database '{_dbPath}' is unreadable ({ex.Message}); re-analyzing from scratch.");
            return Array.Empty<MusicTrack>();
        }
        return result;
    }

    /// <summary>
    /// Removes one track from the catalog by path. The explicit deletion path (the JSON store erased a
    /// removed track implicitly by rewriting the whole file); with per-row upsert this is how a removed
    /// or missing file leaves the catalog without one process clobbering another's rows (doc 31 M1).
    /// </summary>
    public async Task DeleteTrackAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM tracks WHERE path = $path;";
            command.Parameters.AddWithValue("$path", path);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task SaveVisualAsync(IEnumerable<VisualAsset> assets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assets);
        List<VisualAsset> list = assets.ToList();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                @"INSERT INTO visual_assets(path, data) VALUES($path, $data)
                  ON CONFLICT(path) DO UPDATE SET data = excluded.data;";
            SqliteParameter path = command.Parameters.Add("$path", SqliteType.Text);
            SqliteParameter data = command.Parameters.Add("$data", SqliteType.Text);
            foreach (VisualAsset asset in list)
            {
                path.Value = asset.File.Path;
                data.Value = JsonSerializer.Serialize(asset, SerializerOptions);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            transaction.Commit();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<VisualAsset>> LoadVisualAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<VisualAsset>();
        try
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT data FROM visual_assets;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                try
                {
                    VisualAsset? asset = JsonSerializer.Deserialize<VisualAsset>(reader.GetString(0), SerializerOptions);
                    if (asset is not null)
                        result.Add(asset);
                }
                catch (JsonException ex)
                {
                    _onWarning?.Invoke($"Skipping an unreadable visual-catalog row in '{_dbPath}' ({ex.Message}).");
                }
            }
        }
        catch (SqliteException ex)
        {
            _onWarning?.Invoke($"Visual catalog database '{_dbPath}' is unreadable ({ex.Message}).");
            return Array.Empty<VisualAsset>();
        }
        return result;
    }

    public Task SaveScanFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
        => SaveFoldersAsync(ScanKind, folders, cancellationToken);

    public Task<IReadOnlyList<string>> LoadScanFoldersAsync(CancellationToken cancellationToken = default)
        => LoadFoldersAsync(ScanKind, cancellationToken);

    public Task SaveVisualScanFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
        => SaveFoldersAsync(VisualScanKind, folders, cancellationToken);

    public Task<IReadOnlyList<string>> LoadVisualScanFoldersAsync(CancellationToken cancellationToken = default)
        => LoadFoldersAsync(VisualScanKind, cancellationToken);

    public Task SaveSampleFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
        => SaveFoldersAsync(SampleKind, folders, cancellationToken);

    public Task<IReadOnlyList<string>> LoadSampleFoldersAsync(CancellationToken cancellationToken = default)
        => LoadFoldersAsync(SampleKind, cancellationToken);

    private async Task SaveFoldersAsync(string kind, IEnumerable<string> folders, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        List<string> list = folders.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            // Folder lists are tiny and "replace the whole list" is the right semantics, so delete the
            // kind then re-insert. This is not the cross-process race concern (rarely written).
            using (SqliteCommand delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM folders WHERE kind = $kind;";
                delete.Parameters.AddWithValue("$kind", kind);
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            using (SqliteCommand insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = "INSERT OR IGNORE INTO folders(kind, path) VALUES($kind, $path);";
                SqliteParameter kindParam = insert.Parameters.Add("$kind", SqliteType.Text);
                SqliteParameter pathParam = insert.Parameters.Add("$path", SqliteType.Text);
                foreach (string folder in list)
                {
                    kindParam.Value = kind;
                    pathParam.Value = folder;
                    await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            transaction.Commit();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<IReadOnlyList<string>> LoadFoldersAsync(string kind, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<string>();
        try
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT path FROM folders WHERE kind = $kind ORDER BY path;";
            command.Parameters.AddWithValue("$kind", kind);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                result.Add(reader.GetString(0));
        }
        catch (SqliteException ex)
        {
            _onWarning?.Invoke($"Folder list '{kind}' in '{_dbPath}' is unreadable ({ex.Message}).");
            return Array.Empty<string>();
        }
        return result;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;
        await _initGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                @"CREATE TABLE IF NOT EXISTS tracks (path TEXT PRIMARY KEY, schema_version INTEGER NOT NULL, data TEXT NOT NULL);
                  CREATE TABLE IF NOT EXISTS visual_assets (path TEXT PRIMARY KEY, data TEXT NOT NULL);
                  CREATE TABLE IF NOT EXISTS folders (kind TEXT NOT NULL, path TEXT NOT NULL, PRIMARY KEY(kind, path));";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private SqliteConnection OpenConnection()
    {
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // No pooling so the file handle is released promptly (a pooled handle blocks deleting the DB
            // on Windows and keeps the WAL from checkpointing); SQLite open is cheap.
            Pooling = false,
        }.ConnectionString;

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        using SqliteCommand pragma = connection.CreateCommand();
        // WAL = concurrent readers + one serialized cross-process writer; busy_timeout makes a second
        // writer wait instead of throwing SQLITE_BUSY.
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    public void Dispose()
    {
        _writeGate.Dispose();
        _initGate.Dispose();
    }
}
