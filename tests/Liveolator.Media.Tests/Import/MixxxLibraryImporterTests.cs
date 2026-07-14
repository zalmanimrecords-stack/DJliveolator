using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Liveolator.Core.Library.Import;
using Liveolator.Media.Import.Mixxx;
using Xunit;

namespace Liveolator.Media.Tests.Import;

public class MixxxLibraryImporterTests
{
    // Builds a minimal mixxxdb.sqlite (the subset of Mixxx's real schema the importer reads) with one
    // track, two cues, a playlist and a crate, then returns the folder holding it.
    private static string BuildDb()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"mixxx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string dbPath = Path.Combine(dir, "mixxxdb.sqlite");

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Pooling = false, // release the file handle on dispose so the temp dir can be deleted
        }.ConnectionString;
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        Exec(connection,
            """
            CREATE TABLE track_locations (id INTEGER PRIMARY KEY, location TEXT, fs_deleted INTEGER);
            CREATE TABLE library (id INTEGER PRIMARY KEY, location INTEGER, samplerate INTEGER, title TEXT,
                artist TEXT, album TEXT, genre TEXT, year TEXT, bpm REAL, key TEXT, duration REAL, mixxx_deleted INTEGER);
            CREATE TABLE cues (id INTEGER PRIMARY KEY, track_id INTEGER, type INTEGER, position INTEGER,
                hotcue INTEGER, label TEXT, color INTEGER);
            CREATE TABLE Playlists (id INTEGER PRIMARY KEY, name TEXT, position INTEGER, hidden INTEGER);
            CREATE TABLE PlaylistTracks (id INTEGER PRIMARY KEY, playlist_id INTEGER, track_id INTEGER, position INTEGER);
            CREATE TABLE crates (id INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE crate_tracks (crate_id INTEGER, track_id INTEGER);

            INSERT INTO track_locations (id, location, fs_deleted) VALUES (10, 'C:\Music\x.mp3', 0);
            INSERT INTO library (id, location, samplerate, title, artist, album, genre, year, bpm, key, duration, mixxx_deleted)
                VALUES (1, 10, 44100, 'X', 'A', 'Alb', 'Techno', '2024', 128.0, '8A', 200.0, 0);
            -- 2822400 stereo samples / (2*44100) = 32.0 s; color 0xFFFF3B30 (AARRGGBB) = 4294916912.
            INSERT INTO cues (id, track_id, type, position, hotcue, label, color) VALUES (1, 1, 1, 2822400, 0, 'Drop', 4294916912);
            INSERT INTO cues (id, track_id, type, position, hotcue, label, color) VALUES (2, 1, 2, 0, -1, '', 4294901760);
            INSERT INTO Playlists (id, name, position, hidden) VALUES (1, 'Set', 0, 0);
            INSERT INTO Playlists (id, name, position, hidden) VALUES (2, 'Auto', 1, 2);
            INSERT INTO PlaylistTracks (id, playlist_id, track_id, position) VALUES (1, 1, 1, 1);
            INSERT INTO crates (id, name) VALUES (1, 'Crate');
            INSERT INTO crate_tracks (crate_id, track_id) VALUES (1, 1);
            """);
        SqliteConnection.ClearAllPools(); // release the file handle so the temp dir can be deleted
        return dir;
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Parse_ReadsTracksCuesPlaylistsAndCrates()
    {
        string dir = BuildDb();
        try
        {
            LibraryImport import = new MixxxLibraryImporter().Parse(dir);

            ImportedTrack track = Assert.Single(import.Tracks);
            Assert.Equal(@"C:\Music\x.mp3", track.SourcePath);
            Assert.Equal("X", track.Title);
            Assert.Equal(128.0, track.Bpm);
            Assert.Equal("8A", track.Key);
            Assert.Equal(200.0, track.DurationSeconds);

            ImportedCue hot = Assert.Single(track.Cues!, c => !c.IsMemoryCue);
            Assert.Equal(0, hot.Index);
            Assert.Equal(32.0, hot.PositionSeconds, precision: 3); // 2822400 / (2*44100)
            Assert.Equal("Drop", hot.Label);
            Assert.Equal(0xFF3B30, hot.Color);                     // alpha stripped from 0xFFFF3B30
            Assert.Contains(track.Cues!, c => c.IsMemoryCue);      // the main cue → memory/primary

            // Real playlist imported; the hidden (AutoDJ) playlist is excluded. Crate imported too.
            Assert.Contains(import.Playlists, p => p.Name == "Set");
            Assert.DoesNotContain(import.Playlists, p => p.Name == "Auto");
            Assert.Contains(import.Playlists, p => p.Name == "Crate");
            Assert.All(import.Playlists, p => Assert.Equal(@"C:\Music\x.mp3", p.SourceTrackPaths.Single()));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    // Best-effort temp cleanup: SQLite can briefly hold the file handle after dispose on Windows, so a
    // failed delete must not fail the test (the assertions above are what matter; the OS reaps temp dirs).
    private static void TryDeleteDir(string dir)
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // Builds a minimal DB with a single hot cue, letting the test set the track samplerate and the cue's
    // stored color so the color-sentinel and missing-samplerate edges can be exercised in isolation.
    private static string BuildDbWithCue(string sampleRateSql, long cueColor)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"mixxx-edge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string dbPath = Path.Combine(dir, "mixxxdb.sqlite");
        var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ConnectionString;
        using (var connection = new SqliteConnection(cs))
        {
            connection.Open();
            Exec(connection,
                $"""
                CREATE TABLE track_locations (id INTEGER PRIMARY KEY, location TEXT, fs_deleted INTEGER);
                CREATE TABLE library (id INTEGER PRIMARY KEY, location INTEGER, samplerate INTEGER, title TEXT,
                    artist TEXT, album TEXT, genre TEXT, year TEXT, bpm REAL, key TEXT, duration REAL, mixxx_deleted INTEGER);
                CREATE TABLE cues (id INTEGER PRIMARY KEY, track_id INTEGER, type INTEGER, position INTEGER,
                    hotcue INTEGER, label TEXT, color INTEGER);
                CREATE TABLE Playlists (id INTEGER PRIMARY KEY, name TEXT, position INTEGER, hidden INTEGER);
                CREATE TABLE PlaylistTracks (id INTEGER PRIMARY KEY, playlist_id INTEGER, track_id INTEGER, position INTEGER);
                CREATE TABLE crates (id INTEGER PRIMARY KEY, name TEXT);
                CREATE TABLE crate_tracks (crate_id INTEGER, track_id INTEGER);
                INSERT INTO track_locations (id, location, fs_deleted) VALUES (10, 'C:\Music\x.mp3', 0);
                INSERT INTO library (id, location, samplerate, title, artist, album, genre, year, bpm, key, duration, mixxx_deleted)
                    VALUES (1, 10, {sampleRateSql}, 'X', 'A', 'Alb', 'Techno', '2024', 128.0, '8A', 200.0, 0);
                INSERT INTO cues (id, track_id, type, position, hotcue, label, color) VALUES (1, 1, 1, 88200, 0, 'Drop', {cueColor});
                """);
        }
        SqliteConnection.ClearAllPools();
        return dir;
    }

    [Fact]
    public void Parse_CueWithNegativeColorSentinel_HasNoColor_NotWhite()
    {
        // Mixxx stores -1 for "no color assigned"; masking it with 0xFFFFFF would wrongly yield white.
        string dir = BuildDbWithCue("44100", cueColor: -1);
        try
        {
            ImportedTrack track = Assert.Single(new MixxxLibraryImporter().Parse(dir).Tracks);
            ImportedCue hot = Assert.Single(track.Cues!, c => !c.IsMemoryCue);
            Assert.Null(hot.Color);
        }
        finally { TryDeleteDir(dir); }
    }

    [Fact]
    public void Parse_TrackWithoutSamplerate_StillImportsCues_AtDefaultRate()
    {
        // A track row with no stored samplerate must not silently drop every cue (Media iron rule #3) —
        // cue times fall back to the 44.1 kHz default: 88200 / (2 * 44100) = 1.0 s.
        string dir = BuildDbWithCue("NULL", cueColor: 4294916912);
        try
        {
            ImportedTrack track = Assert.Single(new MixxxLibraryImporter().Parse(dir).Tracks);
            ImportedCue hot = Assert.Single(track.Cues!, c => !c.IsMemoryCue);
            Assert.Equal(1.0, hot.PositionSeconds, precision: 3);
        }
        finally { TryDeleteDir(dir); }
    }

    [Fact]
    public void Parse_NoDatabaseInFolder_IsEmpty()
        => Assert.Empty(new MixxxLibraryImporter().Parse(Path.GetTempPath()).Tracks);
}
