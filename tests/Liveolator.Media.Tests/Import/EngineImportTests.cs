using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.Data.Sqlite;
using Liveolator.Core.Library.Import;
using Liveolator.Media.Import.Engine;
using Xunit;

namespace Liveolator.Media.Tests.Import;

// Builds Engine DJ's exact BLOB layouts (clean-room from the documented byte spec) so the readers are
// tested against real structures, including the mixed BE-header / LE-marker endianness.
internal static class EngineFixtures
{
    // beatData: sampleRate(double BE), samples(double BE), isBeatgridSet(u8), then default + adjusted grid.
    public static byte[] BeatDataRaw(double sampleRate, (double Offset, long Beat)[] markers)
    {
        using var ms = new MemoryStream();
        WriteDoubleBE(ms, sampleRate);
        WriteDoubleBE(ms, 16_000_000); // total samples (unused by the reader)
        ms.WriteByte(1);               // isBeatgridSet
        WriteGrid(ms, markers);        // default grid
        WriteGrid(ms, markers);        // adjusted grid (reader prefers this)
        return ms.ToArray();
    }

    private static void WriteGrid(Stream s, (double Offset, long Beat)[] markers)
    {
        WriteInt64BE(s, markers.Length);
        foreach ((double offset, long beat) in markers)
        {
            WriteDoubleLE(s, offset);   // marker fields are LITTLE-endian
            WriteInt64LE(s, beat);
            WriteInt32LE(s, 4);         // beatsToNext (unused)
            WriteInt32LE(s, 0);         // unknown (unused)
        }
    }

    // quickCues: count(int64 BE), then per cue: labelLen(u8), label, sampleOffset(double BE), A,R,G,B.
    public static byte[] QuickCuesRaw(params (string? Label, double Offset, int Argb)[] cues)
    {
        using var ms = new MemoryStream();
        WriteInt64BE(ms, cues.Length);
        foreach ((string? label, double offset, int argb) in cues)
        {
            byte[] labelBytes = label is null ? Array.Empty<byte>() : System.Text.Encoding.UTF8.GetBytes(label);
            ms.WriteByte((byte)labelBytes.Length);
            ms.Write(labelBytes);
            WriteDoubleBE(ms, offset);
            ms.WriteByte((byte)((argb >> 24) & 0xFF)); // A
            ms.WriteByte((byte)((argb >> 16) & 0xFF)); // R
            ms.WriteByte((byte)((argb >> 8) & 0xFF));  // G
            ms.WriteByte((byte)(argb & 0xFF));         // B
        }
        return ms.ToArray();
    }

    // Qt qCompress framing: 4-byte BIG-endian uncompressed length + a zlib stream.
    public static byte[] QCompress(byte[] raw)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw, 0, raw.Length);
        byte[] body = compressed.ToArray();
        byte[] framed = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(framed, raw.Length);
        body.CopyTo(framed, 4);
        return framed;
    }

    private static void WriteDoubleBE(Stream s, double v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteDoubleBigEndian(b, v); s.Write(b); }
    private static void WriteDoubleLE(Stream s, double v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteDoubleLittleEndian(b, v); s.Write(b); }
    private static void WriteInt64BE(Stream s, long v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(b, v); s.Write(b); }
    private static void WriteInt64LE(Stream s, long v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteInt64LittleEndian(b, v); s.Write(b); }
    private static void WriteInt32LE(Stream s, int v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, v); s.Write(b); }
}

public class EngineKeyTests
{
    [Theory]
    [InlineData(0, "8B")]   // C major
    [InlineData(1, "8A")]   // A minor
    [InlineData(23, "7A")]  // documented endpoint
    public void ToCamelot_MatchesDocumentedAnchors(int key, string expected)
        => Assert.Equal(expected, EngineKey.ToCamelot(key));

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void ToCamelot_OutOfRange_IsNull(int key) => Assert.Null(EngineKey.ToCamelot(key));
}

public class EngineBeatDataReaderTests
{
    [Fact]
    public void Read_DerivesBpmAndAnchor_FromMixedEndianMarkers()
    {
        // 4 beats across 2 s at 44100 → 120 BPM; first marker at beat 0 → anchor 0 s.
        byte[] raw = EngineFixtures.BeatDataRaw(44_100, new (double, long)[] { (0, 0), (88_200, 4) });

        EngineGrid grid = EngineBeatDataReader.Read(raw)!.Value;

        Assert.Equal(44_100, grid.SampleRate, precision: 1);
        Assert.Equal(120.0, grid.Bpm, precision: 3);
        Assert.Equal(0.0, grid.FirstBeatSeconds, precision: 4);
    }

    [Fact]
    public void Read_WithAnOverflowingMarkerCount_ReturnsNull_WithoutThrowing()
    {
        // A crafted/corrupt blob whose marker count is so large that count * 24 (MarkerSize) overflows a
        // signed Int64 and wraps negative. The bounds check must reject it rather than letting the wrapped
        // product slip past a naive `p + count*size > length` test and read off the end of the buffer.
        using var ms = new MemoryStream();
        Span<byte> b8 = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(b8, 44_100); ms.Write(b8); // sampleRate
        BinaryPrimitives.WriteDoubleBigEndian(b8, 1_000); ms.Write(b8);  // samples (unused)
        ms.WriteByte(1);                                                 // isBeatgridSet
        BinaryPrimitives.WriteInt64BigEndian(b8, long.MaxValue / 8); ms.Write(b8); // malicious marker count
        byte[] raw = ms.ToArray();

        EngineGrid? grid = null;
        Exception? ex = Record.Exception(() => grid = EngineBeatDataReader.Read(raw));

        Assert.Null(ex);
        Assert.Null(grid);
    }
}

public class EngineQuickCuesReaderTests
{
    [Fact]
    public void Read_ParsesSetCues_AndSkipsUnset()
    {
        byte[] raw = EngineFixtures.QuickCuesRaw(
            ("Drop", 88_200, unchecked((int)0xFFFF3B30)),
            (null, -1, 0)); // unset slot

        EngineCue cue = Assert.Single(EngineQuickCuesReader.Read(raw));
        Assert.Equal(0, cue.Index);
        Assert.Equal(88_200, cue.SampleOffset);
        Assert.Equal("Drop", cue.Label);
        Assert.Equal(0xFF3B30, cue.Color); // alpha dropped
    }
}

public class EngineBlobTests
{
    [Fact]
    public void Inflate_RoundTripsQCompressFraming()
    {
        byte[] raw = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        Assert.Equal(raw, EngineBlob.Inflate(EngineFixtures.QCompress(raw)));
    }

    [Fact]
    public void Inflate_Garbage_IsNull() => Assert.Null(EngineBlob.Inflate(new byte[] { 0, 0, 0, 4, 9, 9 }));
}

public class EngineLibraryImporterTests
{
    private static string BuildDb(int major)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"engine-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string dbPath = Path.Combine(dir, "m.db");

        byte[] beat = EngineFixtures.QCompress(
            EngineFixtures.BeatDataRaw(44_100, new (double, long)[] { (0, 0), (88_200, 4) }));
        byte[] cues = EngineFixtures.QCompress(
            EngineFixtures.QuickCuesRaw(("Drop", 88_200, unchecked((int)0xFFFF3B30)), (null, -1, 0)));

        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ConnectionString;
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        Exec(connection,
            $"""
            CREATE TABLE Information (schemaVersionMajor INTEGER, schemaVersionMinor INTEGER, schemaVersionPatch INTEGER);
            INSERT INTO Information VALUES ({major}, 0, 0);
            CREATE TABLE Playlist (id INTEGER PRIMARY KEY, title TEXT);
            CREATE TABLE PlaylistEntity (id INTEGER PRIMARY KEY, listId INTEGER, trackId INTEGER, nextEntityId INTEGER);
            INSERT INTO Playlist VALUES (1, 'Set');
            INSERT INTO PlaylistEntity VALUES (10, 1, 1, 0);
            """);

        if (major >= 3)
        {
            Exec(connection,
                """
                CREATE TABLE Track (id INTEGER PRIMARY KEY, path TEXT, title TEXT, artist TEXT, album TEXT,
                    genre TEXT, year INTEGER, length INTEGER, bpm INTEGER, bpmAnalyzed REAL, key INTEGER);
                CREATE TABLE PerformanceData (trackId INTEGER PRIMARY KEY, beatData BLOB, quickCues BLOB);
                INSERT INTO Track VALUES (1, 'C:\Music\x.mp3', 'X', 'A', 'Alb', 'Techno', 2024, 200, 120, 120.0, 0);
                """);
            using SqliteCommand pd = connection.CreateCommand();
            pd.CommandText = "INSERT INTO PerformanceData VALUES (1, $beat, $cues)";
            pd.Parameters.AddWithValue("$beat", beat);
            pd.Parameters.AddWithValue("$cues", cues);
            pd.ExecuteNonQuery();
        }
        else
        {
            Exec(connection,
                """
                CREATE TABLE Track (id INTEGER PRIMARY KEY, path TEXT, title TEXT, artist TEXT, album TEXT,
                    genre TEXT, year INTEGER, length INTEGER, bpm INTEGER, bpmAnalyzed REAL, key INTEGER,
                    beatData BLOB, quickCues BLOB);
                """);
            using SqliteCommand t = connection.CreateCommand();
            t.CommandText =
                "INSERT INTO Track VALUES (1, 'C:\\Music\\x.mp3', 'X', 'A', 'Alb', 'Techno', 2024, 200, 120, 120.0, 0, $beat, $cues)";
            t.Parameters.AddWithValue("$beat", beat);
            t.Parameters.AddWithValue("$cues", cues);
            t.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        return dir;
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Theory]
    [InlineData(2)] // blobs on Track
    [InlineData(3)] // blobs in PerformanceData
    public void Parse_ReadsTrackCuesGridAndPlaylist_ForSchema(int major)
    {
        string dir = BuildDb(major);
        try
        {
            LibraryImport import = new EngineLibraryImporter().Parse(dir);

            ImportedTrack track = Assert.Single(import.Tracks);
            Assert.Equal(@"C:\Music\x.mp3", track.SourcePath);
            Assert.Equal("X", track.Title);
            Assert.Equal(120.0, track.Bpm!.Value, precision: 2); // derived from beatData markers
            Assert.Equal("8B", track.Key);                        // key id 0 → 8B
            Assert.Equal(0.0, track.FirstBeatSeconds!.Value, precision: 4);

            ImportedCue cue = Assert.Single(track.Cues!);
            Assert.Equal(0, cue.Index);
            Assert.Equal(2.0, cue.PositionSeconds, precision: 3);  // 88200 samples / 44100
            Assert.Equal("Drop", cue.Label);
            Assert.Equal(0xFF3B30, cue.Color);

            ImportedPlaylist set = Assert.Single(import.Playlists);
            Assert.Equal("Set", set.Name);
            Assert.Equal(@"C:\Music\x.mp3", set.SourceTrackPaths.Single());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Parse_WithACorruptBlob_StillImportsTheTrack_DegradedNotFatal()
    {
        // A truncated/garbage beatData + quickCues blob must degrade THIS track's grid/cues to nothing,
        // never abort the whole import (the class's "a bad blob is skipped, never fatal" contract).
        string dir = Path.Combine(Path.GetTempPath(), $"engine-corrupt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string dbPath = Path.Combine(dir, "m.db");
            var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ConnectionString;
            using (var connection = new SqliteConnection(cs))
            {
                connection.Open();
                Exec(connection,
                    """
                    CREATE TABLE Information (schemaVersionMajor INTEGER, schemaVersionMinor INTEGER, schemaVersionPatch INTEGER);
                    INSERT INTO Information VALUES (2, 0, 0);
                    CREATE TABLE Track (id INTEGER PRIMARY KEY, path TEXT, title TEXT, artist TEXT, album TEXT,
                        genre TEXT, year INTEGER, length INTEGER, bpm INTEGER, bpmAnalyzed REAL, key INTEGER,
                        beatData BLOB, quickCues BLOB);
                    """);
                using SqliteCommand t = connection.CreateCommand();
                t.CommandText =
                    "INSERT INTO Track VALUES (1, 'C:\\Music\\x.mp3', 'X', 'A', 'Alb', 'Techno', 2024, 200, 120, 124.0, 0, $beat, $cues)";
                t.Parameters.AddWithValue("$beat", new byte[] { 0, 0, 0, 64, 9, 9, 9 }); // valid framing, garbage zlib
                t.Parameters.AddWithValue("$cues", new byte[] { 1, 2, 3 });             // too short to inflate
                t.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            LibraryImport import = new EngineLibraryImporter().Parse(dir);

            ImportedTrack track = Assert.Single(import.Tracks);
            Assert.Equal(@"C:\Music\x.mp3", track.SourcePath);
            Assert.Equal(124.0, track.Bpm!.Value, precision: 2); // falls back to the analyzed BPM column
            Assert.Null(track.FirstBeatSeconds);                  // no grid recovered from the bad blob
            Assert.True(track.Cues is null || track.Cues.Count == 0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Parse_MissingDatabase_IsEmpty()
        => Assert.Empty(new EngineLibraryImporter().Parse(Path.GetTempPath()).Tracks);
}
