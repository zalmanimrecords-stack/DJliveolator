using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Liveolator.Core.Library.Import;

namespace Liveolator.Media.Import.Engine;

/// <summary>
/// Imports a Denon/InMusic Engine DJ library from its <c>Database2/m.db</c> (read-only). Clean-room from
/// the libdjinterop schema/encoder docs + the Mixxx "Engine Library Format" wiki. Track metadata is flat
/// columns on <c>Track</c>; cues/beatgrids are qCompress-framed BLOBs (<c>quickCues</c>, <c>beatData</c>)
/// living on <c>Track</c> in schema v2 or in a <c>PerformanceData</c> table in v3 (same blob layout).
/// Playlists are a <c>Playlist</c>/<c>PlaylistEntity</c> linked list. Folder-based: point it at the
/// Engine Library root, the <c>Database2</c> folder, or the <c>m.db</c> file. Tolerant — a missing DB or a
/// bad blob is skipped, never fatal.
/// </summary>
/// <remarks>
/// Schema scope: v2 + v3 (modern Engine DJ 2.x/3.x). Built and unit-tested against crafted fixtures from
/// the documented layout; a real <c>m.db</c> is still the recommended final validation gate (the key table
/// + color semantics + minor-version column drift are best confirmed against real data).
/// </remarks>
public sealed class EngineLibraryImporter : IFolderLibraryImporter
{
    public string FormatName => "Engine DJ";

    public LibraryImport Parse(string rootFolderPath)
    {
        string? dbPath = ResolveDbPath(rootFolderPath);
        if (dbPath is null)
            return LibraryImport.Empty;

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false, // release the file handle; never hold a lock on the user's DB
        }.ConnectionString;

        try
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            int major = ReadSchemaMajor(connection);
            if (major < 2)
                return LibraryImport.Empty; // legacy 1.x uses a different metadata model — not supported

            Dictionary<long, TrackRow> rows = ReadTracks(connection, major);
            var idToPath = rows.ToDictionary(kv => kv.Key, kv => kv.Value.Path);

            var tracks = rows.Values.Select(r => r.ToImportedTrack()).ToList();
            return new LibraryImport(tracks, ReadPlaylists(connection, idToPath));
        }
        catch (SqliteException)
        {
            // A schema we can't read (unexpected version/columns) degrades to nothing imported.
            return LibraryImport.Empty;
        }
    }

    private static string? ResolveDbPath(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return null;
        if (File.Exists(root))
            return root;
        foreach (string candidate in new[]
                 {
                     Path.Combine(root, "m.db"),
                     Path.Combine(root, "Database2", "m.db"),
                     Path.Combine(root, "Engine Library", "Database2", "m.db"),
                 })
            if (File.Exists(candidate))
                return candidate;
        return null;
    }

    private static int ReadSchemaMajor(SqliteConnection connection)
    {
        try
        {
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT schemaVersionMajor FROM Information LIMIT 1";
            return cmd.ExecuteScalar() is long major ? (int)major : 0;
        }
        catch (SqliteException)
        {
            return 0; // no Information table → not an Engine DB we recognize
        }
    }

    private sealed class TrackRow
    {
        public long Id;
        public string Path = string.Empty;
        public string? Title;
        public string? Artist;
        public string? Album;
        public string? Genre;
        public int? Year;
        public double? DurationSeconds;
        public int? KeyId;
        public double? TagBpm;
        public double? AnalyzedBpm;
        public byte[]? BeatData;
        public byte[]? QuickCues;

        public ImportedTrack ToImportedTrack()
        {
            EngineGrid? grid = ReadGridSafe(BeatData);
            double sampleRate = grid?.SampleRate ?? 44_100.0;

            IReadOnlyList<ImportedCue>? cues = ReadCuesSafe(QuickCues, sampleRate);

            double? bpm = grid?.Bpm ?? (AnalyzedBpm is > 0 ? AnalyzedBpm : TagBpm is > 0 ? TagBpm : null);

            return new ImportedTrack(
                SourcePath: Path,
                Title: Title,
                Artist: Artist,
                Album: Album,
                Genre: Genre,
                Year: Year,
                DurationSeconds: DurationSeconds,
                Bpm: bpm,
                FirstBeatSeconds: grid?.FirstBeatSeconds,
                Key: KeyId is { } k ? EngineKey.ToCamelot(k) : null,
                Cues: cues);
        }

        // The per-track BLOBs are user data and may be truncated or corrupt. A bad blob must degrade THIS
        // track's grid/cues to nothing, never abort the whole import (the class contract). The readers are
        // bounds-guarded, so this catch is a belt-and-suspenders backstop for any unforeseen malformed input.
        private static EngineGrid? ReadGridSafe(byte[]? beatData)
        {
            try
            {
                return beatData is { } bd && EngineBlob.Inflate(bd) is { } beat
                    ? EngineBeatDataReader.Read(beat)
                    : null;
            }
            catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException)
            {
                return null;
            }
        }

        private static IReadOnlyList<ImportedCue>? ReadCuesSafe(byte[]? quickCues, double sampleRate)
        {
            try
            {
                if (quickCues is { } qc && EngineBlob.Inflate(qc) is { } quick)
                    return EngineQuickCuesReader.Read(quick)
                        .Select(c => new ImportedCue(c.Index, c.SampleOffset / sampleRate, c.Label, c.Color))
                        .ToList();
                return null;
            }
            catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException)
            {
                return null;
            }
        }
    }

    private static Dictionary<long, TrackRow> ReadTracks(SqliteConnection connection, int major)
    {
        // v2: blobs are columns on Track. v3: blobs live in PerformanceData (joined on trackId).
        string blobSource = major >= 3
            ? "LEFT JOIN PerformanceData p ON p.trackId = t.id"
            : string.Empty;
        string blobCols = major >= 3 ? "p.beatData, p.quickCues" : "t.beatData, t.quickCues";

        var rows = new Dictionary<long, TrackRow>();
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            $"""
            SELECT t.id, t.path, t.title, t.artist, t.album, t.genre, t.year, t.length,
                   t.bpm, t.bpmAnalyzed, t.key, {blobCols}
            FROM Track t {blobSource}
            """;
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string? path = GetString(reader, 1);
            if (string.IsNullOrWhiteSpace(path))
                continue;
            rows[reader.GetInt64(0)] = new TrackRow
            {
                Id = reader.GetInt64(0),
                Path = path,
                Title = GetString(reader, 2),
                Artist = GetString(reader, 3),
                Album = GetString(reader, 4),
                Genre = GetString(reader, 5),
                Year = GetInt(reader, 6),
                DurationSeconds = GetInt(reader, 7),
                TagBpm = GetDouble(reader, 8),
                AnalyzedBpm = GetDouble(reader, 9),
                KeyId = GetInt(reader, 10),
                BeatData = GetBlob(reader, 11),
                QuickCues = GetBlob(reader, 12),
            };
        }
        return rows;
    }

    // Playlists order their tracks as a linked list (PlaylistEntity.nextEntityId; 0 = end) rather than an
    // index column, so we follow the chain from its head to recover the order.
    private static IReadOnlyList<ImportedPlaylist> ReadPlaylists(
        SqliteConnection connection, IReadOnlyDictionary<long, string> idToPath)
    {
        var names = new List<(long Id, string Name)>();
        try
        {
            using SqliteCommand list = connection.CreateCommand();
            list.CommandText = "SELECT id, title FROM Playlist";
            using SqliteDataReader reader = list.ExecuteReader();
            while (reader.Read())
                names.Add((reader.GetInt64(0), GetString(reader, 1) ?? $"Playlist {reader.GetInt64(0)}"));
        }
        catch (SqliteException)
        {
            return Array.Empty<ImportedPlaylist>(); // no playlist tables on this schema
        }

        var entitiesByList = ReadEntities(connection);
        var result = new List<ImportedPlaylist>();
        foreach ((long id, string name) in names)
        {
            if (!entitiesByList.TryGetValue(id, out List<Entity>? entities) || entities.Count == 0)
                continue;
            var paths = OrderByChain(entities)
                .Select(trackId => idToPath.TryGetValue(trackId, out string? p) ? p : null)
                .Where(p => p is not null)
                .Select(p => p!)
                .ToList();
            if (paths.Count > 0)
                result.Add(new ImportedPlaylist(name, paths));
        }
        return result;
    }

    private readonly record struct Entity(long Id, long TrackId, long NextId);

    private static Dictionary<long, List<Entity>> ReadEntities(SqliteConnection connection)
    {
        var byList = new Dictionary<long, List<Entity>>();
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT listId, id, trackId, nextEntityId FROM PlaylistEntity";
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            long listId = reader.GetInt64(0);
            if (!byList.TryGetValue(listId, out List<Entity>? entities))
                byList[listId] = entities = new List<Entity>();
            entities.Add(new Entity(reader.GetInt64(1), reader.GetInt64(2), GetInt64(reader, 3) ?? 0));
        }
        return byList;
    }

    private static IEnumerable<long> OrderByChain(List<Entity> entities)
    {
        var byId = entities.ToDictionary(e => e.Id);
        var referenced = entities.Select(e => e.NextId).ToHashSet();
        // The head is the entity no other entity points to; if the chain is broken/cyclic, fall back to
        // the natural insertion order rather than dropping the playlist.
        Entity? head = entities.FirstOrDefault(e => !referenced.Contains(e.Id)) is { Id: not 0 } h ? h : (Entity?)null;
        if (head is null)
            return entities.Select(e => e.TrackId);

        var ordered = new List<long>(entities.Count);
        var visited = new HashSet<long>();
        Entity? current = head;
        while (current is { } e && visited.Add(e.Id))
        {
            ordered.Add(e.TrackId);
            current = e.NextId != 0 && byId.TryGetValue(e.NextId, out Entity next) ? next : null;
        }
        return ordered.Count == entities.Count ? ordered : entities.Select(e => e.TrackId);
    }

    private static string? GetString(SqliteDataReader reader, int i)
    {
        if (reader.IsDBNull(i))
            return null;
        string s = reader.GetString(i);
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static int? GetInt(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : (int)reader.GetInt64(i);
    private static long? GetInt64(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetInt64(i);
    private static double? GetDouble(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetDouble(i);
    private static byte[]? GetBlob(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : (byte[])reader[i];
}
