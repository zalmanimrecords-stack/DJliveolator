using System.IO;
using System.Linq;
using System.Text;
using Liveolator.Core.Library.Import;
using Liveolator.Media.Import;
using Xunit;

namespace Liveolator.Media.Tests.Import;

public class VirtualDjXmlImporterTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <VirtualDJ_Database Version="2024">
          <Song FilePath="D:\Music\one.mp3" FileSize="123">
            <Tags Author="Artist" Title="One" Album="Alb" Genre="House" Year="2024" Bpm="128" Key="8A" />
            <Infos SongLength="212.5" />
            <Scan Version="801" Bpm="0.46875" Key="Am" />
            <Poi Pos="0.512" Type="beatgrid" />
            <Poi Name="Intro" Pos="0.512" Num="1" />
            <Poi Name="Drop" Pos="32.512" Num="2" />
            <Poi Pos="3.72" Type="automix" Point="fadeStart" />
          </Song>
          <Song FilePath="netsearch://stream/x">
            <Tags Title="net" />
          </Song>
        </VirtualDJ_Database>
        """;

    private static LibraryImport Parse(string xml) =>
        new VirtualDjXmlImporter().Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    [Fact]
    public void Parse_SkipsNetStreamEntries_AndReadsLocalTrack()
    {
        ImportedTrack track = Assert.Single(Parse(Xml).Tracks); // the netsearch:// entry is skipped

        Assert.Equal(@"D:\Music\one.mp3", track.SourcePath);
        Assert.Equal("One", track.Title);
        Assert.Equal("Artist", track.Artist);
        Assert.Equal("House", track.Genre);
        Assert.Equal(2024, track.Year);
        Assert.Equal(212.5, track.DurationSeconds);
        Assert.Equal("8A", track.Key);
    }

    [Fact]
    public void Parse_ConvertsScanSecondsPerBeatToBpm()
    {
        // Scan Bpm="0.46875" is seconds-per-beat → 60 / 0.46875 = 128 BPM (the format's #1 gotcha).
        Assert.Equal(128.0, Parse(Xml).Tracks.Single().Bpm);
    }

    [Fact]
    public void Parse_ReadsHotCues_GridAnchor_AndSkipsAutomix()
    {
        ImportedTrack track = Parse(Xml).Tracks.Single();

        Assert.Equal(0.512, track.FirstBeatSeconds); // the beatgrid POI
        Assert.Equal(2, track.Cues!.Count);          // two hot cues; automix POI skipped
        Assert.Equal(0, track.Cues[0].Index);        // Num=1 -> 0-based slot 0
        Assert.Equal("Intro", track.Cues[0].Label);
        Assert.Equal(1, track.Cues[1].Index);        // Num=2 -> slot 1
        Assert.Equal(32.512, track.Cues[1].PositionSeconds);
    }

    [Fact]
    public void Parse_NumberlessCues_BecomeMemoryCues_NotAllSlotZero()
    {
        // Two POIs with no Num must NOT both collapse onto hot-cue slot 0 (which would overwrite each
        // other and shadow a real Num=1 cue). They route to the memory/primary cue instead.
        string xml = """
            <VirtualDJ_Database><Song FilePath="C:\a.mp3">
              <Poi Name="A" Pos="1.0" />
              <Poi Name="B" Pos="2.0" />
              <Poi Name="Cue1" Pos="3.0" Num="1" />
            </Song></VirtualDJ_Database>
            """;

        ImportedTrack track = Parse(xml).Tracks.Single();

        // The real Num=1 cue keeps hot slot 0; no numberless POI collides with it.
        ImportedCue hot = Assert.Single(track.Cues!, c => c.Index == 0);
        Assert.Equal("Cue1", hot.Label);
        // Both numberless POIs are memory cues, not slot 0.
        Assert.Equal(2, track.Cues!.Count(c => c.IsMemoryCue));
        Assert.DoesNotContain(track.Cues!, c => c.Index == 0 && c.Label != "Cue1");
    }

    [Fact]
    public void Parse_FallsBackToTagsBpm_WhenNoScan()
    {
        string xml = """
            <VirtualDJ_Database><Song FilePath="C:\a.mp3"><Tags Bpm="124" /></Song></VirtualDJ_Database>
            """;
        Assert.Equal(124.0, Parse(xml).Tracks.Single().Bpm);
    }
}
