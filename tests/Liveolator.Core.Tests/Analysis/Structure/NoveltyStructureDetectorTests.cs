using System.Collections.Generic;
using System.Linq;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Analysis.Structure;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Studio.Set;
using Liveolator.Core.Tests.Analysis.Cues;
using Liveolator.Core.Tests.Studio.Set;
using Xunit;

namespace Liveolator.Core.Tests.Analysis.Structure;

public class NoveltyStructureDetectorTests
{
    private const int Sr = CueTestSignals.SampleRate;

    // CueTestSignals.StructuredClickTrack: intro 2-10, drop 10-26, breakdown 26-34,
    // build-up riser 34-38, second drop 38-46, silence 46-48.
    private static BandEnergyFrames StructuredBands() =>
        new BandEnergyEnvelope().Compute(CueTestSignals.StructuredClickTrack(), Sr);

    [Fact]
    public void Detect_EmptyBands_ReturnsNull()
        => Assert.Null(new NoveltyStructureDetector().Detect(BandEnergyFrames.Empty));

    [Fact]
    public void Detect_FlatSignal_ReturnsNull()
    {
        // A steady tone has no change points; the caller must fall back to the rule-based detector
        // rather than receive a structure with a single speculative section.
        BandEnergyFrames bands = new BandEnergyEnvelope().Compute(TestSignals.Sine(440, Sr, seconds: 60), Sr);

        Assert.Null(new NoveltyStructureDetector().Detect(bands));
    }

    [Fact]
    public void Detect_ShortUniformClickTrain_ReturnsNull()
    {
        // Ten seconds of unchanging material is not a song structure — this is the guard that keeps
        // TrackAnalyzer's built-in pass silent on clips too short to hold sections.
        BandEnergyFrames bands = new BandEnergyEnvelope()
            .Compute(TestSignals.ClickTrain(120, Sr, seconds: 10), Sr);

        Assert.Null(new NoveltyStructureDetector().Detect(bands));
    }

    [Fact]
    public void Detect_StructuredTrack_FindsTheArrangementChanges()
    {
        SongStructure? structure = new NoveltyStructureDetector().Detect(StructuredBands());

        Assert.NotNull(structure);
        double[] starts = structure!.Ordered.Select(s => s.StartSeconds).ToArray();

        Assert.Equal(0.0, starts[0]);                       // intro always opens the structure
        Assert.Contains(starts, t => t is > 8.0 and < 12.0);   // kick enters ~10 s
        Assert.Contains(starts, t => t is > 24.0 and < 28.0);  // kick drops out ~26 s
    }

    [Fact]
    public void Detect_StructuredTrack_LabelsTheDropAndTheBreakdown()
    {
        SongStructure? structure = new NoveltyStructureDetector().Detect(StructuredBands());

        Assert.NotNull(structure);
        Assert.Equal(SongSectionLabel.Intro, structure!.Ordered[0].Label);
        Assert.Equal(SongSectionLabel.Drop, LabelNear(structure, 10.0));
        Assert.Equal(SongSectionLabel.Breakdown, LabelNear(structure, 26.0));
    }

    [Fact]
    public void Detect_StructuredTrack_SectionsAreOrderedAndSpaced()
    {
        var detector = new NoveltyStructureDetector(minSpacingSeconds: 12.0);

        SongStructure? structure = detector.Detect(StructuredBands());

        Assert.NotNull(structure);
        double[] starts = structure!.Ordered.Select(s => s.StartSeconds).ToArray();
        for (int i = 1; i < starts.Length; i++)
        {
            Assert.True(starts[i] > starts[i - 1], $"sections out of order at {i}: {starts[i - 1]} -> {starts[i]}");
            if (i > 1)
                Assert.True(starts[i] - starts[i - 1] >= 12.0 - 0.2,
                    $"boundaries {starts[i - 1]} and {starts[i]} are closer than the minimum spacing");
        }
    }

    [Fact]
    public void Detect_WithBeatGrid_PutsEveryBoundaryOnABarLine()
    {
        // SetTransitionPlanner rejects a structure whose boundaries drift off the grid it is mixing on,
        // so snapping is what makes this interchangeable with the beat-synchronous librosa output.
        var grid = new BeatGrid(120.0, DownbeatSeconds: 2.0, BeatsPerBar: 4, Confidence: 0.9);

        SongStructure? structure = new NoveltyStructureDetector().Detect(StructuredBands(), grid);

        Assert.NotNull(structure);
        foreach (SongSection section in structure!.Ordered.Skip(1))   // the intro is anchored at 0, not snapped
        {
            Assert.Equal(grid.NearestDownbeatTo(section.StartSeconds), section.StartSeconds, precision: 6);
        }
    }

    [Fact]
    public void Detect_WithoutBeatGrid_KeepsRawNoveltyPositions()
    {
        // No tempo means nothing to snap to; the detector must still produce its raw structure.
        SongStructure? structure = new NoveltyStructureDetector().Detect(StructuredBands(), BeatGrid.None);

        Assert.NotNull(structure);
        Assert.True(structure!.Ordered.Count >= 3);
    }

    [Fact]
    public void Detect_SameInputTwice_ProducesIdenticalSections()
    {
        // The result is cached to the catalog, so equal-strength candidates must not reorder between runs.
        var detector = new NoveltyStructureDetector();
        BandEnergyFrames bands = StructuredBands();

        SongStructure? first = detector.Detect(bands);
        SongStructure? second = detector.Detect(bands);

        Assert.NotNull(first);
        Assert.Equal(first!.Ordered, second!.Ordered);
    }

    [Fact]
    public void Detect_Output_IsTrustedByTheSetTransitionPlanner()
    {
        // The seam that matters: a novelty structure has to clear the same gate the librosa output does,
        // or STUDIO silently falls back to its distance-from-the-tail rule on every track.
        var grid = new BeatGrid(120.0, DownbeatSeconds: 2.0, BeatsPerBar: 4, Confidence: 0.9);
        SongStructure? structure = new NoveltyStructureDetector().Detect(StructuredBands(), grid);
        MusicTrack track = SetTrackFixture.Track(
            "structured.wav", bpm: 120.0, durationSeconds: 48.0, structure: structure, downbeatSeconds: 2.0);

        var warnings = new List<SetWarning>();
        Assert.True(SetTransitionPlanner.IsStructureTrusted(track, warnings),
            $"planner rejected the detected structure: {string.Join(", ", warnings)}");
    }

    [Fact]
    public void Detect_StructuredTrack_ReportsItsOwnProvenance()
        => Assert.Equal(NoveltyStructureDetector.Provenance,
            new NoveltyStructureDetector().Detect(StructuredBands())!.AnalyzedWith);

    private static string LabelNear(SongStructure structure, double seconds)
        => structure.Ordered.First(s => System.Math.Abs(s.StartSeconds - seconds) < 2.5).Label;
}
