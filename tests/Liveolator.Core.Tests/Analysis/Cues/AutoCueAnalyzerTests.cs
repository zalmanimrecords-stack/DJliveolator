using System;
using System.Linq;
using System.Threading.Tasks;
using Liveolator.Core.Analysis.Cues;
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
    public async Task AnalyzeAsync_DecoderCannotHandle_Throws()
    {
        var decoder = new FakeAudioDecoder(CueTestSignals.StructuredClickTrack(), canDecode: false);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => new AutoCueAnalyzer().AnalyzeAsync(decoder, @"C:\Music\track.flac"));
    }
}
