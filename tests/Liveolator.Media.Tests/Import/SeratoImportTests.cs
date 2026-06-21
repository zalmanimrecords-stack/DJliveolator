using System;
using System.IO;
using System.Linq;
using System.Text;
using Liveolator.Core.Library.Import;
using Liveolator.Media.Import.Serato;
using Xunit;

namespace Liveolator.Media.Tests.Import;

// Builds the exact Serato binary layouts (clean-room from the public format docs) so the readers are
// tested against real byte structures, not mocks.
internal static class SeratoFixtures
{
    public static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);
    public static byte[] Utf16BE(string s) => Encoding.BigEndianUnicode.GetBytes(s);

    public static byte[] Int32BE(int v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

    public static byte[] Synchsafe(int v) =>
        new[] { (byte)((v >> 21) & 0x7F), (byte)((v >> 14) & 0x7F), (byte)((v >> 7) & 0x7F), (byte)(v & 0x7F) };

    public static byte[] FloatBE(float f)
    {
        byte[] b = BitConverter.GetBytes(f);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(b);
        return b;
    }

    public static byte[] Markers2Payload(int index, uint positionMs, int rgb, string name)
    {
        var body = new MemoryStream();
        body.WriteByte(0);                       // pad
        body.WriteByte((byte)index);             // index
        body.Write(Int32BE((int)positionMs));    // position (ms, BE)
        body.WriteByte(0);                       // pad
        body.WriteByte((byte)((rgb >> 16) & 0xFF));
        body.WriteByte((byte)((rgb >> 8) & 0xFF));
        body.WriteByte((byte)(rgb & 0xFF));      // RGB
        body.Write(new byte[] { 0, 0 });         // pad
        body.Write(Encoding.UTF8.GetBytes(name));
        body.WriteByte(0);                       // name null terminator
        byte[] bodyBytes = body.ToArray();

        var inner = new MemoryStream();
        inner.Write(new byte[] { 1, 1 });        // inner version
        inner.Write(Ascii("CUE"));
        inner.WriteByte(0);                      // entry name null terminator
        inner.Write(Int32BE(bodyBytes.Length));
        inner.Write(bodyBytes);
        inner.WriteByte(0);                      // entries terminator

        string base64 = Convert.ToBase64String(inner.ToArray());
        var outer = new MemoryStream();
        outer.Write(new byte[] { 1, 1 });        // outer version
        outer.Write(Ascii(base64));
        outer.WriteByte(0);                      // base64 terminator
        return outer.ToArray();
    }

    public static byte[] BeatGridPayload(float positionSeconds, float bpm)
    {
        var ms = new MemoryStream();
        ms.Write(new byte[] { 1, 0 });   // version
        ms.Write(Int32BE(1));            // 1 marker
        ms.Write(FloatBE(positionSeconds));
        ms.Write(FloatBE(bpm));
        ms.WriteByte(0x37);              // footer
        return ms.ToArray();
    }

    public static byte[] GeobFrameBody(string description, byte[] payload)
    {
        var ms = new MemoryStream();
        ms.WriteByte(0);                                   // text encoding: ISO-8859-1
        ms.Write(Ascii("application/octet-stream"));
        ms.WriteByte(0);                                   // mime null terminator
        ms.WriteByte(0);                                   // empty filename + null terminator
        ms.Write(Ascii(description));
        ms.WriteByte(0);                                   // description null terminator
        ms.Write(payload);
        return ms.ToArray();
    }

    public static byte[] Id3WithGeob(params (string Description, byte[] Payload)[] frames)
    {
        var body = new MemoryStream();
        foreach ((string description, byte[] payload) in frames)
        {
            byte[] frameBody = GeobFrameBody(description, payload);
            body.Write(Ascii("GEOB"));
            body.Write(Synchsafe(frameBody.Length)); // ID3v2.4 synchsafe frame size
            body.Write(new byte[] { 0, 0 });          // frame flags
            body.Write(frameBody);
        }
        byte[] bodyBytes = body.ToArray();

        var ms = new MemoryStream();
        ms.Write(Ascii("ID3"));
        ms.Write(new byte[] { 4, 0, 0 });    // v2.4.0, flags 0
        ms.Write(Synchsafe(bodyBytes.Length));
        ms.Write(bodyBytes);
        return ms.ToArray();
    }

    public static byte[] Crate(params string[] volumeRelativePaths)
    {
        var ms = new MemoryStream();
        byte[] vrsn = Utf16BE("1.0/Serato ScratchLive Crate");
        ms.Write(Ascii("vrsn"));
        ms.Write(Int32BE(vrsn.Length));
        ms.Write(vrsn);

        foreach (string path in volumeRelativePaths)
        {
            byte[] ptrkBody = Utf16BE(path);
            var otrk = new MemoryStream();
            otrk.Write(Ascii("ptrk"));
            otrk.Write(Int32BE(ptrkBody.Length));
            otrk.Write(ptrkBody);
            byte[] otrkBytes = otrk.ToArray();

            ms.Write(Ascii("otrk"));
            ms.Write(Int32BE(otrkBytes.Length));
            ms.Write(otrkBytes);
        }
        return ms.ToArray();
    }
}

public class SeratoBeatGridReaderTests
{
    [Fact]
    public void Read_ParsesAnchorAndBpm()
    {
        SeratoGrid grid = SeratoBeatGridReader.Read(SeratoFixtures.BeatGridPayload(0.305f, 115.0f))!.Value;
        Assert.Equal(0.305, grid.FirstBeatSeconds, precision: 3);
        Assert.Equal(115.0, grid.Bpm, precision: 3);
    }

    [Fact]
    public void Read_ParsesTheDocumentedTestVector()
    {
        // 01 00 | 00 00 00 01 | 3e 9c 28 38 (=0.3050s) | 42 e6 00 00 (=115.0) | 37
        byte[] payload =
        {
            0x01, 0x00, 0x00, 0x00, 0x00, 0x01, 0x3e, 0x9c, 0x28, 0x38, 0x42, 0xe6, 0x00, 0x00, 0x37,
        };
        SeratoGrid grid = SeratoBeatGridReader.Read(payload)!.Value;
        Assert.Equal(115.0, grid.Bpm, precision: 2);
        Assert.Equal(0.305, grid.FirstBeatSeconds, precision: 3);
    }

    [Fact]
    public void Read_Garbage_IsNull() => Assert.Null(SeratoBeatGridReader.Read(new byte[] { 0xAB, 0xCD }));
}

public class SeratoMarkers2ReaderTests
{
    [Fact]
    public void ReadCues_ParsesIndexPositionColorAndName()
    {
        byte[] payload = SeratoFixtures.Markers2Payload(index: 1, positionMs: 64_000, rgb: 0xFF0000, name: "Drop");

        SeratoCue cue = Assert.Single(SeratoMarkers2Reader.ReadCues(payload));
        Assert.Equal(1, cue.Index);
        Assert.Equal(64_000, cue.PositionMs);
        Assert.Equal(0xFF0000, cue.Color);
        Assert.Equal("Drop", cue.Name);
    }

    [Fact]
    public void ReadCues_Garbage_IsEmpty() => Assert.Empty(SeratoMarkers2Reader.ReadCues(new byte[] { 9, 9, 9 }));
}

public class SeratoCrateReaderTests
{
    [Fact]
    public void ReadTrackPaths_ReturnsEveryOtrkPath()
    {
        byte[] crate = SeratoFixtures.Crate("Music/a.mp3", "Music/House/b.mp3");

        Assert.Equal(new[] { "Music/a.mp3", "Music/House/b.mp3" }, SeratoCrateReader.ReadTrackPaths(crate));
    }
}

public class Id3GeobReaderTests
{
    [Fact]
    public void ReadGeobFrames_ExtractsPayloadByDescription()
    {
        byte[] grid = SeratoFixtures.BeatGridPayload(0.1f, 120f);
        byte[] markers = SeratoFixtures.Markers2Payload(0, 1000, 0x00FF00, "Intro");
        byte[] id3 = SeratoFixtures.Id3WithGeob(("Serato BeatGrid", grid), ("Serato Markers2", markers));

        var frames = Id3GeobReader.ReadGeobFrames(new MemoryStream(id3));

        Assert.Equal(grid, frames["Serato BeatGrid"]);
        Assert.Equal(markers, frames["Serato Markers2"]);
    }

    [Fact]
    public void ReadGeobFrames_NonId3_IsEmpty()
        => Assert.Empty(Id3GeobReader.ReadGeobFrames(new MemoryStream(new byte[] { 1, 2, 3, 4, 5 })));
}

public class SeratoLibraryImporterTests
{
    [Fact]
    public void Parse_ReadsCuesAndGridFromFiles_AndCratesAsPlaylists()
    {
        string root = Path.Combine(Path.GetTempPath(), $"serato-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "_Serato_", "Subcrates"));
        try
        {
            byte[] id3 = SeratoFixtures.Id3WithGeob(
                ("Serato BeatGrid", SeratoFixtures.BeatGridPayload(0.2f, 128f)),
                ("Serato Markers2", SeratoFixtures.Markers2Payload(0, 32_000, 0xFF3B30, "Drop")));
            File.WriteAllBytes(Path.Combine(root, "song.mp3"), id3);
            File.WriteAllBytes(
                Path.Combine(root, "_Serato_", "Subcrates", "My Set.crate"),
                SeratoFixtures.Crate("song.mp3"));

            LibraryImport import = new SeratoLibraryImporter().Parse(root);

            ImportedTrack track = Assert.Single(import.Tracks);
            Assert.EndsWith("song.mp3", track.SourcePath);
            Assert.Equal(128.0, track.Bpm!.Value, precision: 2);
            Assert.Equal(0.2, track.FirstBeatSeconds!.Value, precision: 3);
            ImportedCue cue = Assert.Single(track.Cues!);
            Assert.Equal(0, cue.Index);
            Assert.Equal(32.0, cue.PositionSeconds, precision: 3); // 32000 ms -> seconds
            Assert.Equal("Drop", cue.Label);
            Assert.Equal(0xFF3B30, cue.Color);

            ImportedPlaylist set = Assert.Single(import.Playlists);
            Assert.Equal("My Set", set.Name);
            Assert.EndsWith("song.mp3", set.SourceTrackPaths.Single());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Parse_MissingFolder_IsEmpty()
        => Assert.Empty(new SeratoLibraryImporter().Parse(@"X:\does\not\exist").Tracks);
}
