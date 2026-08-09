using Liveolator.Core.Analysis.Structure;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Studio.Set;
using Xunit;

namespace Liveolator.Core.Tests.Studio.Set;

/// <summary>
/// The musical judgement in a set: where to leave a record, where to enter the next one, and how long to
/// hold them together — including every case where the structure analysis must not be believed.
/// </summary>
public class SetTransitionPlannerTests
{
    private const double PhraseSeconds = 30.0;   // 16 bars at 128 BPM
    private const double BarSeconds = 1.875;

    private static readonly SetBuildOptions Options = new();

    private static List<SetWarning> NoWarnings() => new();

    [Fact]
    public void PlanMixOut_LeavesOnTheOutro_WhenTheStructureIsTrustworthy()
    {
        MusicTrack track = SetTrackFixture.Track("a.mp3", structure: SetTrackFixture.StandardStructure());

        MixAnchor? anchor = SetTransitionPlanner.PlanMixOut(track, overlapBars: 16, earliestSeconds: 0.0, NoWarnings());

        Assert.NotNull(anchor);
        Assert.Equal(240.0, anchor!.SourceSeconds, 3);
        Assert.Equal(SongSectionLabel.Outro, anchor.SectionLabel);
        Assert.Equal(AnchorSource.Structure, anchor.Source);
    }

    [Fact]
    public void PlanMixOut_NeverLeavesBeforeTheLastDrop()
    {
        // A late second drop leaves no valid structural exit; the fallback must still land after it rather
        // than on the earlier breakdown, or the record gets cut at its peak.
        SongStructure structure = SetTrackFixture.Structure(
            new SongSection(0.0, SongSectionLabel.Intro),
            new SongSection(60.0, SongSectionLabel.Breakdown),
            new SongSection(90.0, SongSectionLabel.Drop),
            new SongSection(210.0, SongSectionLabel.Drop));
        MusicTrack track = SetTrackFixture.Track("a.mp3", structure: structure);

        MixAnchor? anchor = SetTransitionPlanner.PlanMixOut(track, overlapBars: 16, earliestSeconds: 0.0, NoWarnings());

        Assert.NotNull(anchor);
        Assert.True(anchor!.SourceSeconds >= 210.0, $"mix-out {anchor.SourceSeconds}s cuts the last drop at 210s");
    }

    [Fact]
    public void PlanMixOut_IgnoresStructure_WithTooFewSections()
    {
        SongStructure thin = SetTrackFixture.Structure(
            new SongSection(0.0, SongSectionLabel.Intro),
            new SongSection(90.0, SongSectionLabel.Drop));
        MusicTrack track = SetTrackFixture.Track("a.mp3", structure: thin);
        var warnings = NoWarnings();

        MixAnchor? anchor = SetTransitionPlanner.PlanMixOut(track, overlapBars: 16, earliestSeconds: 0.0, warnings);

        Assert.NotNull(anchor);
        Assert.Equal(AnchorSource.Fallback, anchor!.Source);
        Assert.Contains(SetWarning.StructureRejected, warnings);
    }

    [Fact]
    public void PlanMixOut_IgnoresStructure_WhoseBoundariesMissTheBeatGrid()
    {
        // Sections computed against a different grid than the one we mix on cannot be used for placement.
        SongStructure drifted = SetTrackFixture.Structure(
            new SongSection(1.1, SongSectionLabel.Intro),
            new SongSection(61.1, SongSectionLabel.BuildUp),
            new SongSection(91.1, SongSectionLabel.Drop),
            new SongSection(241.1, SongSectionLabel.Outro));
        MusicTrack track = SetTrackFixture.Track("a.mp3", structure: drifted);
        var warnings = NoWarnings();

        MixAnchor? anchor = SetTransitionPlanner.PlanMixOut(track, overlapBars: 16, earliestSeconds: 0.0, warnings);

        Assert.Equal(AnchorSource.Fallback, anchor!.Source);
        Assert.Contains(SetWarning.StructureRejected, warnings);
    }

    [Fact]
    public void PlanMixOut_WarnsAndFallsBack_WithNoStructureAtAll()
    {
        MusicTrack track = SetTrackFixture.Track("a.mp3");
        var warnings = NoWarnings();

        MixAnchor? anchor = SetTransitionPlanner.PlanMixOut(track, overlapBars: 16, earliestSeconds: 0.0, warnings);

        Assert.NotNull(anchor);
        Assert.Equal(AnchorSource.Fallback, anchor!.Source);
        Assert.Contains(SetWarning.NoStructure, warnings);
        Assert.DoesNotContain(SetWarning.StructureRejected, warnings);
    }

    [Fact]
    public void PlanMixOut_AlwaysLeavesRoomForTheWholeBlend()
    {
        MusicTrack track = SetTrackFixture.Track("a.mp3", durationSeconds: 200.0);

        MixAnchor? anchor = SetTransitionPlanner.PlanMixOut(track, overlapBars: 32, earliestSeconds: 0.0, NoWarnings());

        Assert.NotNull(anchor);
        Assert.True(anchor!.SourceSeconds + (32 * BarSeconds) <= 200.0, "the blend must finish before the file does");
    }

    [Fact]
    public void PlanMixOut_QuantizesToThePhraseGrid()
    {
        // An off-phrase section start must be pulled back onto the track's own phrase line.
        SongStructure offPhrase = SetTrackFixture.Structure(
            new SongSection(0.0, SongSectionLabel.Intro),
            new SongSection(90.0, SongSectionLabel.Drop),
            new SongSection(213.75, SongSectionLabel.Breakdown),
            new SongSection(232.5, SongSectionLabel.Outro));
        MusicTrack track = SetTrackFixture.Track("a.mp3", structure: offPhrase);

        MixAnchor? anchor = SetTransitionPlanner.PlanMixOut(track, overlapBars: 16, earliestSeconds: 0.0, NoWarnings());

        Assert.NotNull(anchor);
        Assert.Equal(0.0, anchor!.SourceSeconds % PhraseSeconds, 6);
    }

    [Fact]
    public void PlanMixIn_EntersOnTheIntro_WhenTheStructureIsTrustworthy()
    {
        MusicTrack track = SetTrackFixture.Track("b.mp3", structure: SetTrackFixture.StandardStructure());
        var warnings = NoWarnings();

        MixAnchor anchor = SetTransitionPlanner.PlanMixIn(track, warnings);

        Assert.Equal(0.0, anchor.SourceSeconds, 3);
        Assert.Equal(AnchorSource.Structure, anchor.Source);
    }

    [Fact]
    public void PlanMixIn_SkipsPastABeatlessIntro_ToWhereTheKickStarts()
    {
        // Opening a blend over 60 s of pad is the classic robot failure; the analyzed kick onsets are
        // already there to prevent it.
        MusicTrack track = SetTrackFixture.Track(
            "b.mp3",
            structure: SetTrackFixture.StandardStructure(),
            kicks: new[] { 60.0, 60.5, 61.0 });
        var warnings = NoWarnings();

        MixAnchor anchor = SetTransitionPlanner.PlanMixIn(track, warnings);

        Assert.Equal(60.0, anchor.SourceSeconds, 3);
        Assert.Contains(SetWarning.NoKickAtMixIn, warnings);
    }

    [Fact]
    public void PlanMixIn_StaysPut_WhenTheKickIsAlreadyThere()
    {
        MusicTrack track = SetTrackFixture.Track(
            "b.mp3",
            structure: SetTrackFixture.StandardStructure(),
            kicks: new[] { 0.5, 1.0, 1.5 });
        var warnings = NoWarnings();

        MixAnchor anchor = SetTransitionPlanner.PlanMixIn(track, warnings);

        Assert.Equal(0.0, anchor.SourceSeconds, 3);
        Assert.DoesNotContain(SetWarning.NoKickAtMixIn, warnings);
    }

    [Fact]
    public void Plan_UsesTheRequestedOverlap_WhenBothRecordsHaveTheRunway()
    {
        MusicTrack from = SetTrackFixture.Track("a.mp3", structure: SetTrackFixture.StandardStructure());
        MusicTrack to = SetTrackFixture.Track("b.mp3", structure: SetTrackFixture.StandardStructure());

        TransitionShape? shape = SetTransitionPlanner.Plan(from, 0.0, to, Options, true, true);

        Assert.NotNull(shape);
        Assert.Equal(16, shape!.OverlapBars);
        Assert.Empty(shape.Warnings);
    }

    [Fact]
    public void Plan_ShortensTheOverlap_WhenTheIncomingRecordIsTight()
    {
        MusicTrack from = SetTrackFixture.Track("a.mp3");
        // 50 s holds the shortest blend (15 s) plus the phrase that must follow it, but not a 16-bar one.
        MusicTrack to = SetTrackFixture.Track("b.mp3", durationSeconds: 50.0);

        TransitionShape? shape = SetTransitionPlanner.Plan(from, 0.0, to, Options, true, true);

        Assert.NotNull(shape);
        Assert.Equal(SetBuildOptions.MinOverlapBars, shape!.OverlapBars);
        Assert.Contains(SetWarning.OverlapClamped, shape.Warnings);
    }

    [Fact]
    public void Plan_CapsTheOverlap_WhenEitherGridCannotBeTrusted()
    {
        MusicTrack from = SetTrackFixture.Track("a.mp3", structure: SetTrackFixture.StandardStructure());
        MusicTrack to = SetTrackFixture.Track("b.mp3", structure: SetTrackFixture.StandardStructure());

        TransitionShape? shape = SetTransitionPlanner.Plan(from, 0.0, to, Options, fromPhaseReady: true, toPhaseReady: false);

        Assert.NotNull(shape);
        Assert.Equal(SetBuildOptions.LowConfidenceOverlapBars, shape!.OverlapBars);
    }

    [Fact]
    public void Plan_ReportsAnIncomingDrop_ThatLandsInsideTheBlend()
    {
        MusicTrack from = SetTrackFixture.Track("a.mp3", structure: SetTrackFixture.StandardStructure());
        // A drop 15 s in lands halfway through a 30 s crossfade — the outgoing record is still over it.
        SongStructure early = SetTrackFixture.Structure(
            new SongSection(0.0, SongSectionLabel.Intro),
            new SongSection(15.0, SongSectionLabel.Drop),
            new SongSection(150.0, SongSectionLabel.Breakdown),
            new SongSection(240.0, SongSectionLabel.Outro));
        MusicTrack to = SetTrackFixture.Track("b.mp3", structure: early);

        TransitionShape? shape = SetTransitionPlanner.Plan(from, 0.0, to, Options, true, true);

        Assert.NotNull(shape);
        Assert.Contains(SetWarning.IncomingDropInsideOverlap, shape!.Warnings);
    }

    [Fact]
    public void Plan_RefusesToMix_WhenTheOutgoingRecordHasNothingLeft()
    {
        MusicTrack from = SetTrackFixture.Track("a.mp3", durationSeconds: 300.0);
        MusicTrack to = SetTrackFixture.Track("b.mp3");

        // Entered 280 s in, there is no room for a phrase plus the shortest blend before the file ends.
        TransitionShape? shape = SetTransitionPlanner.Plan(from, 280.0, to, Options, true, true);

        Assert.Null(shape);
    }

    [Fact]
    public void Plan_KeepsBothAnchorsOnTheirOwnPhraseGrid()
    {
        // The invariant the whole arrangement rests on, checked at a tempo whose phrase is not a round
        // number of seconds (124 BPM ⇒ 30.968 s).
        MusicTrack from = SetTrackFixture.Track("a.mp3", bpm: 124.0, structure: SetTrackFixture.StandardStructure());
        MusicTrack to = SetTrackFixture.Track("b.mp3", bpm: 124.0, structure: SetTrackFixture.StandardStructure());
        double phrase = 16 * 4 * 60.0 / 124.0;

        TransitionShape? shape = SetTransitionPlanner.Plan(from, 0.0, to, Options, true, true);

        Assert.NotNull(shape);
        Assert.Equal(0.0, shape!.Out.SourceSeconds % phrase, 6);
        Assert.Equal(0.0, shape.In.SourceSeconds % phrase, 6);
    }
}
