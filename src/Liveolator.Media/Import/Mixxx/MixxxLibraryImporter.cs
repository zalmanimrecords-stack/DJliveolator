using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Liveolator.Core.Library.Import;

namespace Liveolator.Media.Import.Mixxx;

/// <summary>
/// Imports a Mixxx library from its <c>mixxxdb.sqlite</c> database (read-only). Clean-room from Mixxx's
/// open <c>res/schema.xml</c> + the documented <c>CueType</c> enum (data/schema, not GPL code). Tables:
/// <c>library</c> (track facts incl. <c>samplerate</c>) joined to <c>track_locations</c> for the path;
/// <c>cues</c> (hot cues + the main cue); ordered <c>Playlists</c>/<c>PlaylistTracks</c> and unordered
/// <c>crates</c>/<c>crate_tracks</c>. Cue positions are stereo-interleaved sample offsets, so
/// seconds = position / (2 × samplerate). Folder-based: point it at the folder holding the .sqlite
/// (or the file itself). Tolerant — a missing DB yields an empty import.
/// </summary>
public sealed class MixxxLibraryImporter : IFolderLibraryImporter
{
    private const string DbFileName = "mixxxdb.sqlite";

    // Fallback sample rate for converting cue sample-offsets when a track row has no stored samplerate.
    private const int DefaultSampleRate = 44_100;

    // CueType values stored in the DB: 1 = HotCue, 2 = MainCue. Others (Loop/Intro/Outro/…) are skipped.
    private const int CueTypeHotCue = 1;
    private const int CueTypeMainCue = 2;

    public string FormatName => "Mixxx";

    public LibraryImport Parse(string rootFolderPath)
    {
        string? dbPath = ResolveDbPath(rootFolderPath);
        if (dbPath is null)
            return LibraryImport.Empty;

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false, // one-shot read: release the file handle on dispose, never hold a lock on the user's DB
        }.ConnectionString;

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        Dictionary<long, TrackRow> rows = ReadTracks(connection);
        AttachCues(connection, rows);

        var tracks = new List<ImportedTrack>(rows.Count);
        var idToPath = new Dictionary<long, string>(rows.Count);
        foreach (TrackRow row in rows.Values)
        {
            idToPath[row.Id] = row.Path;
            tracks.Add(new ImportedTrack(
                SourcePath: row.Path,
                Title: row.Title,
                Artist: row.Artist,
                Album: row.Album,
                Genre: row.Genre,
                Year: row.Year,
                DurationSeconds: row.DurationSeconds,
                Bpm: row.Bpm,
                Key: row.Key,
                Cues: row.Cues));
        }

        var playlists = new List<ImportedPlaylist>();
        playlists.AddRange(ReadPlaylists(connection, idToPath));
        playlists.AddRange(ReadCrates(connection, idToPath));
        return new LibraryImport(tracks, playlists);
    }

    private static string? ResolveDbPath(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return null;
        if (File.Exists(root))
            return root; // the .sqlite file was picked directly
        string candidate = Path.Combine(root, DbFileName);
        return File.Exists(candidate) ? candidate : null;
    }

    private sealed class TrackRow
    {
        public long Id;
        public string Path = string.Empty;
        public int SampleRate;
        public string? Title;
        public string? Artist;
        public string? Album;
        public string? Genre;
        public int? Year;
        public double? Bpm;
        public string? Key;
        public double? DurationSeconds;
        public List<ImportedCue> Cues = new();
    }

    private static Dictionary<long, TrackRow> ReadTracks(SqliteConnection connection)
    {
        var rows = new Dictionary<long, TrackRow>();
        using SqliteCommand cmd = connection.CreateCommand();
        // library.location holds track_locations.id (a Mixxx quirk), so the join is on tl.id = l.location.
        cmd.CommandText =
            """
            SELECT l.id, tl.location, l.samplerate, l.title, l.artist, l.album, l.genre, l.year,
                   l.bpm, l.key, l.duration
            FROM library l JOIN track_locations tl ON tl.id = l.location
            WHERE l.mixxx_deleted = 0 AND tl.fs_deleted = 0
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
                SampleRate = GetInt(reader, 2) ?? 0,
                Title = GetString(reader, 3),
                Artist = GetString(reader, 4),
                Album = GetString(reader, 5),
                Genre = GetString(reader, 6),
                Year = ParseYear(GetString(reader, 7)),
                Bpm = GetDouble(reader, 8) is > 0 and var b ? b : null,
                Key = GetString(reader, 9),
                DurationSeconds = GetDouble(reader, 10),
            };
        }
        return rows;
    }

    private static void AttachCues(SqliteConnection connection, Dictionary<long, TrackRow> rows)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT track_id, type, position, hotcue, label, color FROM cues WHERE position >= 0";
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            long trackId = reader.GetInt64(0);
            if (!rows.TryGetValue(trackId, out TrackRow? row))
                continue;

            int type = GetInt(reader, 1) ?? 0;
            long position = reader.GetInt64(2);
            // Stereo-interleaved sample offset → seconds (always ÷2, the engine is always stereo). When the
            // track has no stored samplerate (rare — an un-analyzed row), fall back to the CD-standard
            // 44.1 kHz rather than silently dropping every cue on the track (Media iron rule #3); a track
            // that carries cues is almost always 44.1 kHz, so the estimate is exact for the common case.
            int sampleRate = row.SampleRate > 0 ? row.SampleRate : DefaultSampleRate;
            double seconds = position / (2.0 * sampleRate);
            string? label = GetString(reader, 4);
            int? color = GetColor(reader, 5);

            if (type == CueTypeHotCue)
            {
                int hot = GetInt(reader, 3) ?? -1;
                if (hot >= 0)
                    row.Cues.Add(new ImportedCue(hot, seconds, label, color));
            }
            else if (type == CueTypeMainCue)
            {
                row.Cues.Add(new ImportedCue(ImportedCue.MemoryCue, seconds, label, color));
            }
        }
    }

    private static IEnumerable<ImportedPlaylist> ReadPlaylists(
        SqliteConnection connection, IReadOnlyDictionary<long, string> idToPath)
    {
        var result = new List<ImportedPlaylist>();
        using (SqliteCommand list = connection.CreateCommand())
        {
            list.CommandText = "SELECT id, name FROM Playlists WHERE hidden = 0 ORDER BY position";
            var playlists = new List<(long Id, string Name)>();
            using (SqliteDataReader reader = list.ExecuteReader())
                while (reader.Read())
                    playlists.Add((reader.GetInt64(0), GetString(reader, 1) ?? $"Playlist {reader.GetInt64(0)}"));

            foreach ((long id, string name) in playlists)
            {
                var paths = ReadOrderedMembers(
                    connection,
                    "SELECT track_id FROM PlaylistTracks WHERE playlist_id = $id ORDER BY position",
                    id, idToPath);
                if (paths.Count > 0)
                    result.Add(new ImportedPlaylist(name, paths));
            }
        }
        return result;
    }

    private static IEnumerable<ImportedPlaylist> ReadCrates(
        SqliteConnection connection, IReadOnlyDictionary<long, string> idToPath)
    {
        var result = new List<ImportedPlaylist>();
        using (SqliteCommand list = connection.CreateCommand())
        {
            list.CommandText = "SELECT id, name FROM crates";
            var crates = new List<(long Id, string Name)>();
            using (SqliteDataReader reader = list.ExecuteReader())
                while (reader.Read())
                    crates.Add((reader.GetInt64(0), GetString(reader, 1) ?? $"Crate {reader.GetInt64(0)}"));

            foreach ((long id, string name) in crates)
            {
                var paths = ReadOrderedMembers(
                    connection,
                    "SELECT track_id FROM crate_tracks WHERE crate_id = $id",
                    id, idToPath);
                if (paths.Count > 0)
                    result.Add(new ImportedPlaylist(name, paths));
            }
        }
        return result;
    }

    private static List<string> ReadOrderedMembers(
        SqliteConnection connection, string sql, long id, IReadOnlyDictionary<long, string> idToPath)
    {
        var paths = new List<string>();
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", id);
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
            if (idToPath.TryGetValue(reader.GetInt64(0), out string? path))
                paths.Add(path);
        return paths;
    }

    private static string? GetString(SqliteDataReader reader, int i)
    {
        if (reader.IsDBNull(i))
            return null;
        string s = reader.GetString(i);
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static int? GetInt(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : (int)reader.GetInt64(i);
    private static double? GetDouble(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetDouble(i);

    // Mixxx cue color is 0xAARRGGBB; drop the alpha to our 0xRRGGBB. NULL → no color. Mixxx also stores a
    // negative sentinel (e.g. -1) for "no color assigned" — treat that as no color too, otherwise the
    // & 0xFFFFFF mask would turn -1 into 0xFFFFFF (solid white) on every uncolored cue.
    private static int? GetColor(SqliteDataReader reader, int i)
    {
        if (reader.IsDBNull(i))
            return null;
        long argb = reader.GetInt64(i);
        return argb < 0 ? null : (int)(argb & 0xFFFFFF);
    }

    private static int? ParseYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        string head = value.Split('-', '/')[0];
        return int.TryParse(head, out int year) && year > 0 ? year : null;
    }
}
