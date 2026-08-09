using System;
using System.Linq;
using System.Threading.Tasks;
using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Analysis.Structure;
using Xunit;

namespace Liveolator.Core.Tests.Analysis.Cues;

public class AutoCueAnalyzerTests
{
    private const int Sr = CueTestSignals.SampleRate;

    // 2-bar phrases so the grid is fine enough for the ~48 s synthetic track.
    private static AutoCueAnalyzer Analyzer() =>
        new(structuralDetector: new StructuralCueDetector(phraseBars: 2));

    [Fact]
    public void AnalyzePcm_Silence_ReturnsNull()
    {
        // No audible region -> nothing to cue; the track's cues are left untouched.
        TrackCueSet? result = new AutoCueAnalyzer().AnalyzePcm(new float[Sr], Sr);
        Assert.Null(result);
    }

    [Fact]
    public void AnalyzePcm_StructuredTrack_PlacesAutoCuesIncludingDrop()
    {
        TrackCueSet? result = Analyzer().AnalyzePcm(CueTestSignals.StructuredClickTrack(), Sr);

        Assert.NotNull(result);
        Assert.NotEmpty(result!.HotCues);
        Assert.All(result.HotCues, cue => Assert.True(cue.IsAuto));
        Assert.Contains(result.HotCues, c => c.Label == "Start");
        Assert.Contains(result.HotCues, c => c.Label == "Drop");
    }

    [Fact]
    public async Task AnalyzeAsync_DecodesThenPlacesCues()
    {
        var decoder = new FakeAudioDecoder(CueTestSignals.StructuredClickTrack());

        TrackCueSet? result = await Analyzer().AnalyzeAsync(decoder, @"C:\Music\track.wav");

        Assert.NotNull(result);
        Assert.Contains(result!.HotCues, c => c.Label == "Drop");
    }

    [Fact]
    public void AnalyzePcm_StructureWithoutAnOutro_StillPlacesTheOutroCue()
    {
        // The outro pad is an always-safe slot, not a speculative one. A segmentation that ends on a
        // rise (a track ending in its payload) must not empty it — the heuristic contour fills it in.
        var noOutro = new SongStructure(
            new[]
            {
                new SongSection(0.0, SongSectionLabel.Intro),
                new SongSection(10.0, SongSectionLabel.Drop),
                new SongSection(26.0, SongSectionLabel.Breakdown),
            },
            NoveltyStructureDetector.Provenance);

        TrackCueSet? result = Analyzer().AnalyzePcm(CueTestSignals.StructuredClickTrack(), Sr, noOutro);

        Assert.NotNull(result);
        Assert.Contains(result!.HotCues, c => c.Label == "Outro");
        Assert.Contains(result.HotCues, c => c.Label == "Drop");
    }

    [Fact]
    public void AnalyzePcm_StructureWithAnOutro_KeepsItsOwn()
    {
        const double realOutroSeconds = 40.0;
        var withOutro = new SongStructure(
            new[]
            {
                new SongSection(0.0, SongSectionLabel.Intro),
                new SongSection(10.0, SongSectionLabel.Drop),
                new SongSection(realOutroSeconds, SongSectionLabel.Outro),
            },
            NoveltyStructureDetector.Provenance);

        TrackCueSet? result = Analyzer().AnalyzePcm(CueTestSignals.StructuredClickTrack(), Sr, withOutro);

        HotCue outro = result!.HotCues.Single(c => c.Label == "Outro");
        Assert.InRange((double)outro.PositionSamples / Sr, realOutroSeconds - 0.5, realOutroSeconds + 0.5);
    }

    [Fact]
    public void AnalyzePcm_WithSongStructure_PrefersRealSectionBoundaries()
    {
        // A real drop at a deliberate position the energy heuristic would not pick. The placed Drop cue
        // must land on (the beat-snap of) that real boundary, proving structure wins over the heuristic.
        const double realDropSeconds = 24.0;
        var structure = new SongStructure(
            new[]
            {
                new SongSection(0.0, SongSectionLabel.Intro),
                new SongSection(realDropSeconds, SongSectionLabel.Drop),
            },
            "librosa 0.10.2");

        TrackCueSet? withStructure = Analyzer().AnalyzePcm(CueTestSignals.StructuredClickTrack(), Sr, structure);
        TrackCueSet? heuristic = Analyzer().AnalyzePcm(CueTestSignals.StructuredClickTrack(), Sr);

        Assert.NotNull(withStructure);
        HotCue structDrop = withStructure!.HotCues.Single(c => c.Label == "Drop");
        HotCue heuristicDrop = heuristic!.HotCues.Single(c => c.Label == "Drop");

        double structDropSeconds = (double)structDrop.PositionSamples / Sr;
        Assert.InRange(structDropSeconds, realDropSeconds - 0.5, realDropSeconds + 0.5);
        Assert.NotEqual(heuristicDrop.PositionSamples, structDrop.PositionSamples);
    }

    [Fact]
    public async Task AnalyzeAsync_WithSongStructure_PrefersRealSectionBoundaries()
    {
        var decoder = new FakeAudioDecoder(CueTestSignals.StructuredClickTrack());
        var structure = new SongStructure(
            new[] { new SongSection(0.0, SongSectionLabel.Intro), new SongSection(24.0, SongSectionLabel.Drop) },
            "librosa 0.10.2");

        TrackCueSet? result = await Analyzer().AnalyzeAsync(decoder, @"C:\Music\track.wav", structure);

        Assert.NotNull(result);
        double dropSeconds = (double)result!.HotCues.Single(c => c.Label == "Drop").PositionSamples / Sr;
        Assert.InRange(dropSeconds, 23.5, 24.5);
    }

    [Fact]
    public async Task AnalyzeAsync_DecoderCannotHandle_Throws()
    {
        var decoder = new FakeAudioDecoder(CueTestSignals.StructuredClickTrack(), canDecode: false);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => new AutoCueAnalyzer().AnalyzeAsync(decoder, @"C:\Music\track.flac"));
    }
}
