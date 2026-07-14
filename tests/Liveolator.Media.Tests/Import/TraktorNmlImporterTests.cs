using System.IO;
using System.Linq;
using System.Text;
using Liveolator.Core.Library.Import;
using Liveolator.Media.Import;
using Xunit;

namespace Liveolator.Media.Tests.Import;

public class TraktorNmlImporterTests
{
    private const string Nml = """
        <?xml version="1.0" encoding="UTF-8" standalone="no"?>
        <NML VERSION="19">
          <COLLECTION ENTRIES="1">
            <ENTRY TITLE="Trk" ARTIST="Art">
              <LOCATION DIR="/:Music/:House/:" FILE="song.mp3" VOLUME="C:"/>
              <ALBUM TITLE="Alb"/>
              <INFO GENRE="Techno" KEY="Am" COMMENT="c" PLAYTIME="200" RELEASE_DATE="2019/1/1"/>
              <TEMPO BPM="124.000000"/>
              <MUSICAL_KEY VALUE="21"/>
              <CUE_V2 NAME="Grid" TYPE="4" START="25.4" HOTCUE="-1"/>
              <CUE_V2 NAME="Drop" TYPE="0" START="64000.0" HOTCUE="0"/>
              <CUE_V2 NAME="Mem" TYPE="0" START="500.0" HOTCUE="-1"/>
            </ENTRY>
          </COLLECTION>
          <PLAYLISTS>
            <NODE TYPE="FOLDER" NAME="$ROOT">
              <SUBNODES COUNT="1">
                <NODE TYPE="PLAYLIST" NAME="My Set">
                  <PLAYLIST ENTRIES="1" TYPE="LIST">
                    <ENTRY><PRIMARYKEY TYPE="TRACK" KEY="C:/:Music/:House/:song.mp3"/></ENTRY>
                  </PLAYLIST>
                </NODE>
              </SUBNODES>
            </NODE>
          </PLAYLISTS>
        </NML>
        """;

    private static LibraryImport Parse(string nml) =>
        new TraktorNmlImporter().Parse(new MemoryStream(Encoding.UTF8.GetBytes(nml)));

    [Fact]
    public void Parse_ReadsMetadataBpmAndReconstructsPath()
    {
        ImportedTrack track = Parse(Nml).Tracks.Single();

        Assert.Equal("C:/Music/House/song.mp3", track.SourcePath);
        Assert.Equal("Trk", track.Title);
        Assert.Equal("Art", track.Artist);
        Assert.Equal("Alb", track.Album);
        Assert.Equal("Techno", track.Genre);
        Assert.Equal("c", track.Comment);
        Assert.Equal(2019, track.Year);
        Assert.Equal(124.0, track.Bpm);
        Assert.Equal(200.0, track.DurationSeconds);
        Assert.Equal("Am", track.Key); // INFO@KEY text preferred
    }

    [Fact]
    public void Parse_GridMarkerBecomesAnchor_CuesConvertMsToSeconds()
    {
        ImportedTrack track = Parse(Nml).Tracks.Single();

        Assert.Equal(0.0254, track.FirstBeatSeconds); // grid marker 25.4 ms
        Assert.Equal(2, track.Cues!.Count);           // grid marker is NOT a cue
        Assert.Equal(64.0, track.Cues.Single(c => c.Index == 0).PositionSeconds); // 64000 ms
        Assert.Equal(0.5, track.Cues.Single(c => c.IsMemoryCue).PositionSeconds); // 500 ms
    }

    [Fact]
    public void Parse_FallsBackToMusicalKeyInteger_WhenNoKeyText()
    {
        // VALUE 21 -> pitch class 9 (A), minor -> Camelot 8A.
        string nml = Nml.Replace("KEY=\"Am\" ", string.Empty);
        ImportedTrack track = Parse(nml).Tracks.Single();
        Assert.Equal("8A", track.Key);
    }

    [Fact]
    public void Parse_ReadsNestedPlaylist()
    {
        ImportedPlaylist set = Parse(Nml).Playlists.Single();

        Assert.Equal("My Set", set.Name);
        Assert.Equal("C:/Music/House/song.mp3", set.SourceTrackPaths.Single());
    }
}
