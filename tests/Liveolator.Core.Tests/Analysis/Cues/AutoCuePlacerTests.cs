using System.Collections.Generic;
using Liveolator.Core.Analysis.Cues;
using Xunit;

namespace Liveolator.Core.Tests.Analysis.Cues;

public class AutoCuePlacerTests
{
    private const int Sr = 44100;
    private const double Bpm = 120.0; // 0.5 s/beat -> 22050 samples/beat

    private static StructuralCueResult FullResult() => new(
        new List<StructuralCue>
        {
            new(StructuralCueKind.TrackStart, 2.0, 0.95),
            new(StructuralCueKind.Drop, 10.0, 0.9),
            new(StructuralCueKind.Breakdown, 26.0, 0.9),
            new(StructuralCueKind.BuildUp, 34.0, 0.8),
            new(StructuralCueKind.Phrase, 14.0, 0.6),
            new(StructuralCueKind.Phrase, 18.0, 0.6),
            new(StructuralCueKind.Phrase, 22.0, 0.6),
            new(StructuralCueKind.OutroStart, 44.0, 0.9),
        },
        OverallConfidence: 0.9);

    [Fact]
    public void Place_MapsKindsToBankLayout()
    {
        TrackCueSet set = new AutoCuePlacer().Place(FullResult(), Bpm, Sr);

        Assert.Equal("Start", set.GetHotCue(0)!.Value.Label);
        Assert.Equal("Drop", set.GetHotCue(1)!.Value.Label);
        Assert.Equal("Breakdown", set.GetHotCue(2)!.Value.Label);
        Assert.Equal("Build", set.GetHotCue(3)!.Value.Label);
        Assert.Equal("Phrase", set.GetHotCue(4)!.Value.Label);
        Assert.Equal("Phrase", set.GetHotCue(5)!.Value.Label);
        Assert.Equal("Phrase", set.GetHotCue(6)!.Value.Label);
        Assert.Equal("Outro", set.GetHotCue(7)!.Value.Label);
    }

    [Fact]
    public void Place_MarksAllPlacedCuesAsAuto()
    {
        TrackCueSet set = new AutoCuePlacer().Place(FullResult(), Bpm, Sr);

        Assert.NotEmpty(set.HotCues);
        Assert.All(set.HotCues, cue => Assert.True(cue.IsAuto));
    }

    [Fact]
    public void Place_AssignsColorsPerKind()
    {
        TrackCueSet set = new AutoCuePlacer().Place(FullResult(), Bpm, Sr);

        Assert.Equal(0xFF3B30, set.GetHotCue(1)!.Value.Color); // Drop = red
        Assert.Equal(0x0A84FF, set.GetHotCue(2)!.Value.Color); // Breakdown = blue
    }

    [Fact]
    public void Place_SnapsPositionsToBeatGrid()
    {
        var result = new StructuralCueResult(
            new List<StructuralCue> { new(StructuralCueKind.Drop, 10.1, 0.9) }, 0.9);

        TrackCueSet set = new AutoCuePlacer().Place(result, Bpm, Sr);

        // 10.1 s -> nearest beat (0.5 s grid) is 10.0 s = 441000 samples.
        Assert.Equal(441000, set.RecallSamples(1));
    }

    [Fact]
    public void Place_LowConfidenceSpeculativeCue_LeavesSlotEmpty()
    {
        var result = new StructuralCueResult(
            new List<StructuralCue>
            {
                new(StructuralCueKind.TrackStart, 0.0, 0.95),
                new(StructuralCueKind.Drop, 10.0, 0.3), // below the 0.5 floor
            },
            0.5);

        TrackCueSet set = new AutoCuePlacer().Place(result, Bpm, Sr);

        Assert.True(set.IsHotCueSet(0));   // Start placed
        Assert.False(set.IsHotCueSet(1));  // Drop suppressed
    }

    [Fact]
    public void Place_AlwaysSafePair_BypassesConfidenceFloor()
    {
        // Outro at 0.7 with a high floor still gets placed because it is always-safe.
        var result = new StructuralCueResult(
            new List<StructuralCue>
            {
                new(StructuralCueKind.TrackStart, 0.0, 0.7),
                new(StructuralCueKind.OutroStart, 100.0, 0.7),
            },
            0.7);

        TrackCueSet set = new AutoCuePlacer(minConfidence: 0.9).Place(result, Bpm, Sr);

        Assert.True(set.IsHotCueSet(0));
        Assert.True(set.IsHotCueSet(7));
    }

    [Fact]
    public void Place_EmptyResult_ReturnsEmptySet()
    {
        TrackCueSet set = new AutoCuePlacer().Place(StructuralCueResult.Empty, Bpm, Sr);

        Assert.Empty(set.HotCues);
    }

    [Fact]
    public void Place_PhraseCollidingWithNamedCue_IsSkipped()
    {
        // A phrase at the same position as the drop must not duplicate the drop pad.
        var result = new StructuralCueResult(
            new List<StructuralCue>
            {
                new(StructuralCueKind.Drop, 10.0, 0.9),
                new(StructuralCueKind.Phrase, 10.0, 0.6), // collides with drop
                new(StructuralCueKind.Phrase, 18.0, 0.6), // distinct
            },
            0.9);

        TrackCueSet set = new AutoCuePlacer().Place(result, Bpm, Sr);

        Assert.True(set.IsHotCueSet(1));        // Drop
        Assert.True(set.IsHotCueSet(4));        // the distinct phrase
        Assert.Equal(set.RecallSamples(1), 441000);
        Assert.NotEqual(set.RecallSamples(4), set.RecallSamples(1)); // not a duplicate of the drop
    }

    [Fact]
    public void Place_CapsPhrasesAtThreeSlots()
    {
        var cues = new List<StructuralCue>();
        for (int i = 1; i <= 6; i++)
            cues.Add(new StructuralCue(StructuralCueKind.Phrase, i * 8.0, 0.6));
        var result = new StructuralCueResult(cues, 0.8);

        TrackCueSet set = new AutoCuePlacer().Place(result, Bpm, Sr);

        Assert.True(set.IsHotCueSet(4));
        Assert.True(set.IsHotCueSet(5));
        Assert.True(set.IsHotCueSet(6));
        Assert.False(set.IsHotCueSet(7)); // slot 7 is reserved for the outro, never a phrase
    }
}
