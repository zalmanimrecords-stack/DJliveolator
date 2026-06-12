using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Mixer;
using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.Core.Tests.Studio;

public class TransitionDefaultsTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static MusicTrack Track(string path, double? bpm, TrackCues cues)
        => new(
            new ScannedFile(path, 1000, T),
            bpm is null ? null : new BpmResult(bpm.Value, 0.9),
            new MusicalKey(0, KeyMode.Major, "8B", 0.9),
            TimeSpan.FromMinutes(5),
            cues,
            MediaAnalysisStatus.Ok,
            null);

    private static TrackCues PhraseCues =>
        new(IntroStart: TimeSpan.Zero,
            IntroEnd: TimeSpan.FromSeconds(16),
            OutroStart: TimeSpan.FromMinutes(4),
            OutroEnd: TimeSpan.FromMinutes(5));

    [Fact]
    public void For_MissingFromTempo_ReturnsCut()
    {
        StudioTransition t = TransitionDefaults.For(
            Track("a", bpm: null, TrackCues.None), Track("b", 124, TrackCues.None));

        Assert.Equal(TransitionKind.Cut, t.Kind);
        Assert.Equal(0, t.LengthBeats);
    }

    [Fact]
    public void For_MissingToTempo_ReturnsCut()
    {
        StudioTransition t = TransitionDefaults.For(
            Track("a", 124, TrackCues.None), Track("b", bpm: null, TrackCues.None));

        Assert.Equal(TransitionKind.Cut, t.Kind);
    }

    [Fact]
    public void For_BothTempo_NoPhraseCues_TailOverlapShortBlend()
    {
        StudioTransition t = TransitionDefaults.For(
            Track("a", 124, TrackCues.None), Track("b", 125, TrackCues.None));

        Assert.Equal(TransitionKind.BassSwap, t.Kind);
        Assert.Equal(TransitionAnchor.TailOverlap, t.Anchor);
        Assert.Equal(TransitionDefaults.BlindOverlapBeats, t.LengthBeats);
        Assert.Equal(CrossfaderCurve.Smooth, t.Curve);
    }

    [Fact]
    public void For_BothPhraseCues_AnchorsOutroToIntro_FullPhrase()
    {
        // 'from' needs an outro start; 'to' needs an intro end — together they let us anchor.
        StudioTransition t = TransitionDefaults.For(
            Track("a", 124, PhraseCues), Track("b", 124, PhraseCues));

        Assert.Equal(TransitionAnchor.OutroToIntro, t.Anchor);
        Assert.Equal(TransitionDefaults.PhraseBlendBeats, t.LengthBeats);
    }

    [Fact]
    public void For_OnlyOneSidePhraseCue_FallsBackToTailOverlap()
    {
        // 'from' has an outro cue but 'to' has no intro-end cue → cannot anchor outro→intro.
        var fromWithOutro = Track("a", 124, new TrackCues(null, null, TimeSpan.FromMinutes(4), null));
        var toNoIntroEnd = Track("b", 124, new TrackCues(TimeSpan.Zero, null, null, null));

        StudioTransition t = TransitionDefaults.For(fromWithOutro, toNoIntroEnd);

        Assert.Equal(TransitionAnchor.TailOverlap, t.Anchor);
    }
}
