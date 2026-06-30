using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Analysis.Structure;
using Xunit;

namespace Liveolator.Core.Tests.Analysis.Cues;

public class SongStructureCuesTests
{
    [Fact]
    public void ToStructuralCues_Null_ReturnsNull()
        => Assert.Null(SongStructureCues.ToStructuralCues(null));

    [Fact]
    public void ToStructuralCues_Empty_ReturnsNull()
        => Assert.Null(SongStructureCues.ToStructuralCues(new SongStructure(System.Array.Empty<SongSection>(), "x")));

    [Fact]
    public void ToStructuralCues_MapsLabelsToKinds()
    {
        var structure = new SongStructure(
            new[]
            {
                new SongSection(0.0, SongSectionLabel.Intro),
                new SongSection(16.0, SongSectionLabel.BuildUp),
                new SongSection(32.0, SongSectionLabel.Drop),
                new SongSection(96.0, SongSectionLabel.Breakdown),
                new SongSection(160.0, SongSectionLabel.Outro),
                new SongSection(200.0, SongSectionLabel.Section),
            },
            "librosa 0.10.2");

        StructuralCueResult? result = SongStructureCues.ToStructuralCues(structure);

        Assert.NotNull(result);
        Assert.Contains(result!.Cues, c => c.Kind == StructuralCueKind.TrackStart && c.PositionSeconds == 0.0);
        Assert.Contains(result.Cues, c => c.Kind == StructuralCueKind.BuildUp && c.PositionSeconds == 16.0);
        Assert.Contains(result.Cues, c => c.Kind == StructuralCueKind.Drop && c.PositionSeconds == 32.0);
        Assert.Contains(result.Cues, c => c.Kind == StructuralCueKind.Breakdown && c.PositionSeconds == 96.0);
        Assert.Contains(result.Cues, c => c.Kind == StructuralCueKind.OutroStart && c.PositionSeconds == 160.0);
        Assert.Contains(result.Cues, c => c.Kind == StructuralCueKind.Phrase && c.PositionSeconds == 200.0);
    }

    [Fact]
    public void ToStructuralCues_UnknownLabel_MapsToPhrase()
    {
        var structure = new SongStructure(new[] { new SongSection(5.0, "wobble") }, "librosa 0.10");
        StructuralCueResult? result = SongStructureCues.ToStructuralCues(structure);
        Assert.NotNull(result);
        Assert.All(result!.Cues, c => Assert.Equal(StructuralCueKind.Phrase, c.Kind));
    }

    [Fact]
    public void ToStructuralCues_HighConfidence_SoSpeculativeCuesPass()
    {
        var structure = new SongStructure(new[] { new SongSection(32.0, SongSectionLabel.Drop) }, "librosa 0.10");
        StructuralCueResult? result = SongStructureCues.ToStructuralCues(structure);
        Assert.NotNull(result);
        // Real ML boundaries are trusted: confidence must clear AutoCuePlacer's default 0.5 floor.
        Assert.All(result!.Cues, c => Assert.True(c.Confidence >= 0.5));
    }
}
