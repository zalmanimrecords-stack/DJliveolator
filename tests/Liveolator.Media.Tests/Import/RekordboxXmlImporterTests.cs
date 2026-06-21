using System.IO;
using System.Linq;
using System.Text;
using Liveolator.Core.Library.Import;
using Liveolator.Media.Import;
using Xunit;

namespace Liveolator.Media.Tests.Import;

public class RekordboxXmlImporterTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <DJ_PLAYLISTS Version="1.0.0">
          <COLLECTION Entries="2">
            <TRACK TrackID="1" Name="Track One" Artist="DJ A" Album="Alb" Genre="House" Year="2020"
                   AverageBpm="128.00" Tonality="8A" TotalTime="300" Comments="hi"
                   Location="file://localhost/C:/Music/one.mp3">
              <TEMPO Inizio="0.150" Bpm="128.00" Metro="4/4" Battito="1"/>
              <POSITION_MARK Name="Intro" Type="0" Start="0.5" Num="-1"/>
              <POSITION_MARK Name="Drop" Type="0" Start="64.25" Num="0" Red="255" Green="0" Blue="0"/>
              <POSITION_MARK Name="Loop" Type="4" Start="10" Num="1"/>
            </TRACK>
            <TRACK TrackID="2" Name="Track Two" AverageBpm="120.00"
                   Location="file://localhost/C:/Music/two.mp3"/>
          </COLLECTION>
          <PLAYLISTS>
            <NODE Type="0" Name="ROOT" Count="1">
              <NODE Name="My Set" Type="1" KeyType="0" Entries="2">
                <TRACK Key="1"/>
                <TRACK Key="2"/>
              </NODE>
            </NODE>
          </PLAYLISTS>
        </DJ_PLAYLISTS>
        """;

    private static LibraryImport Parse(string xml) =>
        new RekordboxXmlImporter().Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    [Fact]
    public void Parse_ReadsTrackMetadataBpmGridAndKey()
    {
        ImportedTrack track = Parse(Xml).Tracks.First();

        Assert.EndsWith("one.mp3", track.SourcePath);
        Assert.Equal("Track One", track.Title);
        Assert.Equal("DJ A", track.Artist);
        Assert.Equal("House", track.Genre);
        Assert.Equal(2020, track.Year);
        Assert.Equal(128.0, track.Bpm);
        Assert.Equal(0.150, track.FirstBeatSeconds);
        Assert.Equal("8A", track.Key);
        Assert.Equal(300.0, track.DurationSeconds);
    }

    [Fact]
    public void Parse_ReadsCues_MapsMemoryAndHotCue_SkipsLoops()
    {
        ImportedTrack track = Parse(Xml).Tracks.First();

        Assert.Equal(2, track.Cues!.Count); // the Type=4 loop is skipped
        ImportedCue memory = track.Cues.Single(c => c.IsMemoryCue);
        Assert.Equal(0.5, memory.PositionSeconds);
        ImportedCue drop = track.Cues.Single(c => c.Index == 0);
        Assert.Equal(64.25, drop.PositionSeconds);
        Assert.Equal("Drop", drop.Label);
        Assert.Equal(0xFF0000, drop.Color);
    }

    [Fact]
    public void Parse_ResolvesPlaylistTrackIdsToPaths()
    {
        ImportedPlaylist set = Parse(Xml).Playlists.Single();

        Assert.Equal("My Set", set.Name);
        Assert.Equal(2, set.SourceTrackPaths.Count);
        Assert.All(set.SourceTrackPaths, p => Assert.Contains("Music", p));
    }

    [Fact]
    public void Parse_EmptyOrNonCollection_ReturnsEmpty()
        => Assert.Empty(Parse("<DJ_PLAYLISTS></DJ_PLAYLISTS>").Tracks);
}
