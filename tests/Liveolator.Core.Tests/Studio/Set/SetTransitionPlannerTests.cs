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
    public void PlanMixOut_NeverLeavesOnABreakdown()
    {
        // The measured 2026-08-13 defect: a record with no outro label leaves on its last breakdown, and the
        // join records that record's own hole into the mix (join 1 sat 30.1 dB below its local level).
        SongStructure noOutro = SetTrackFixture.Structure(
            new SongSection(0.0, SongSectionLabel.Intro),
            new SongSection(60.0, SongSectionLabel.BuildUp),
            new SongSection(90.0, SongSectionLabel.Drop),
            new SongSection(150.0, SongSectionLabel.Breakdown),
            new SongSection(180.0, SongSectionLabel.Drop),
            new SongSection(240.0, SongSectionLabel.Breakdown));
        MusicTrack track = SetTrackFixture.Track("a.mp3", structure: noOutro);

        MixAnchor? anchor = SetTransitionPlanner.PlanMixOut(track, overlapBars: 16, earliestSeconds: 0.0, NoWarnings());

        Assert.NotNull(anchor);
        Assert.NotEqual(SongSectionLabel.Breakdown, anchor!.SectionLabel);
    }

    [Fact]
    public void PlanMixOut_FallsBackWithAWarning_WhenTheOnlyLateSectionIsABreakdown()
    {
        SongStructure breakdownExit = SetTrackFixture.Structure(
            new SongSection(0.0, SongSectionLabel.Intro),
            new SongSection(90.0, SongSectionLabel.Drop),
            new SongSection(240.0, SongSectionLabel.Breakdown));
        MusicTrack track = SetTrackFixture.Track("a.mp3", structure: breakdownExit);
        var warnings = NoWarnings();

        MixAnchor? anchor = SetTransitionPlanner.PlanMixOut(track, overlapBars: 16, earliestSeconds: 0.0, warnings);

        Assert.NotNull(anchor);
        Assert.Equal(AnchorSource.Fallback, anchor!.Source);
        Assert.Contains(SetWarning.StructureRejected, warnings);
    }

    [Fact]
    public void PlanMixOut_DoesNotCutAShortRecordAtAQuarter_OnTheFallbackPath()
    {
        // EarliestMixOutFraction was enforced only on the structure branch, and the fallback is the branch
        // that runs for every record without trusted structure. Measured: 30 s of a 120 s record.
        MusicTrack track = SetTrackFixture.Track("a.mp3", durationSeconds: 120.0);

        MixAnchor? anchor = SetTransitionPlanner.PlanMixOut(track, overlapBars: 32, earliestSeconds: 0.0, NoWarnings());

        Assert.NotNull(anchor);
        // 70% is unreachable when the blend alone is half the record, so the rule becomes "take the latest
        // legal exit rather than leaving a tail": half the record, not a quarter of it.
        Assert.True(anchor!.SourceSeconds >= 0.5 * 120.0, $"mix-out {anchor.SourceSeconds}s cuts a 120 s record too early");
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
        Assert.Contains(SetWarning.MixInMovedToKick, warnings);
        Assert.DoesNotContain(SetWarning.NoKickAtMixIn, warnings);
    }

    [Fact]
    public void PlanMixIn_IgnoresAMidTrackIntroLabel()
    {
        // The intro search ran over the whole ordered list, so a re-intro halfway in was taken as the entry
        // point — a random mid-track entry into a by-definition low-energy section, reported as trusted.
        SongStructure reIntro = SetTrackFixture.Structure(
            new SongSection(0.0, SongSectionLabel.BuildUp),
            new SongSection(30.0, SongSectionLabel.Drop),
            new SongSection(150.0, SongSectionLabel.Intro),
            new SongSection(180.0, SongSectionLabel.Drop),
            new SongSection(240.0, SongSectionLabel.Outro));
        MusicTrack track = SetTrackFixture.Track("b.mp3", structure: reIntro);

        MixAnchor anchor = SetTransitionPlanner.PlanMixIn(track, NoWarnings());

        Assert.True(anchor.SourceSeconds < PhraseSeconds, $"entered {anchor.SourceSeconds}s into the record");
    }

    [Fact]
    public void PlanMixIn_NeverOpensOverBeatlessMaterial()
    {
        // The advance snapped DOWN to a phrase line, so "moved onto the kick" could land a full phrase
        // before it — 29 s of pad, while reporting that the entry had been corrected.
        MusicTrack track = SetTrackFixture.Track(
            "b.mp3",
            structure: SetTrackFixture.StandardStructure(),
            kicks: new[] { 89.0, 89.5, 90.0 });

        MixAnchor anchor = SetTransitionPlanner.PlanMixIn(track, NoWarnings());

        Assert.True(
            89.0 - anchor.SourceSeconds <= BarSeconds,
            $"entry {anchor.SourceSeconds}s opens {89.0 - anchor.SourceSeconds}s before the first kick");
    }

    [Fact]
    public void PlanMixIn_RejectsATrackWhoseDrumsStartTooLate()
    {
        // The advance had no cap, so a record whose drums start two minutes in was entered two minutes in —
        // and the outgoing side then had no runway left, which cascaded into every later record being blamed.
        MusicTrack from = SetTrackFixture.Track("a.mp3", structure: SetTrackFixture.StandardStructure());
        MusicTrack to = SetTrackFixture.Track("b.mp3", kicks: new[] { 120.0, 120.5, 121.0 });

        TransitionShape? shape = SetTransitionPlanner.Plan(from, 0.0, to, Options, true, true);

        Assert.Null(shape);
    }

    [Fact]
    public void PlanMixIn_DistinguishesAMovedEntryFromAKicklessOne()
    {
        // One member for both cases is how a warning gets trained to be ignored: it fired on 8 of 9 TATA Box
        // joins as the benign case, hiding the one where the drums never come back.
        var moved = NoWarnings();
        SetTransitionPlanner.PlanMixIn(
            SetTrackFixture.Track("moved.mp3", structure: SetTrackFixture.StandardStructure(), kicks: new[] { 60.0, 60.5 }),
            moved);

        var kickless = NoWarnings();
        SetTransitionPlanner.PlanMixIn(
            SetTrackFixture.Track("kickless.mp3", kicks: new[] { 10.0, 20.0, 30.0 }, downbeatSeconds: 120.0),
            kickless);

        Assert.Contains(SetWarning.MixInMovedToKick, moved);
        Assert.Contains(SetWarning.NoKickAtMixIn, kickless);
        Assert.DoesNotContain(SetWarning.NoKickAtMixIn, moved);
        Assert.DoesNotContain(SetWarning.MixInMovedToKick, kickless);
    }

    [Fact]
    public void PlanMixIn_WarnsWhenKickOnsetsWereNeverAnalyzed()
    {
        // An un-analyzed record used to look identical to a perfect one: entry chosen, no warnings.
        MusicTrack track = SetTrackFixture.Track("b.mp3", structure: SetTrackFixture.StandardStructure());
        var warnings = NoWarnings();

        SetTransitionPlanner.PlanMixIn(track, warnings);

        Assert.Contains(SetWarning.KickOnsetsNotAnalyzed, warnings);
    }

    [Fact]
    public void Plan_KeepsTheStructureAnchorSource_WhenTheKickAdvanceMovesTheEntry()
    {
        // The trust signal was inverted: a well-executed long-blend entry read Fallback while the breakdown
        // mix-out above read Structure.
        MusicTrack from = SetTrackFixture.Track("a.mp3", structure: SetTrackFixture.StandardStructure());
        MusicTrack to = SetTrackFixture.Track(
            "b.mp3", structure: SetTrackFixture.StandardStructure(), kicks: new[] { 60.0, 60.5, 61.0 });

        TransitionShape? shape = SetTransitionPlanner.Plan(from, 0.0, to, Options, true, true);

        Assert.NotNull(shape);
        Assert.Equal(60.0, shape!.In.SourceSeconds, 3);
        Assert.Equal(AnchorSource.Structure, shape.In.Source);
    }

    [Fact]
    public void Plan_DoesNotReportOverlapClamped_WhenEightBarsWasTheMostAllowed()
    {
        // A low-confidence grid caps the blend at 8 bars by design, so calling that a clamp reports a
        // compromise the caller never asked to avoid.
        MusicTrack from = SetTrackFixture.Track("a.mp3", structure: SetTrackFixture.StandardStructure());
        MusicTrack to = SetTrackFixture.UntrustedGrid("b.mp3");

        TransitionShape? shape = SetTransitionPlanner.Plan(from, 0.0, to, Options, fromPhaseReady: true, toPhaseReady: false);

        Assert.NotNull(shape);
        Assert.Equal(SetBuildOptions.LowConfidenceOverlapBars, shape!.OverlapBars);
        Assert.DoesNotContain(SetWarning.OverlapClamped, shape.Warnings);
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
        // Kicks on both records on purpose: a catalog entry with no kick onsets is itself something to
        // report now, so "nothing was compromised" needs a pair that was actually analyzed.
        MusicTrack from = SetTrackFixture.Track(
            "a.mp3", structure: SetTrackFixture.StandardStructure(), kicks: KicksFrom(0.0, 300.0));
        MusicTrack to = SetTrackFixture.Track(
            "b.mp3", structure: SetTrackFixture.StandardStructure(), kicks: KicksFrom(0.0, 300.0));

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
    public void Plan_WarnsWhenAnyDropLandsInsideTheOverlap_NotJustTheFirst()
    {
        // The check looked only at the FIRST drop, so it disabled itself whenever the entry had been advanced
        // past it — which is precisely the configuration that produces a train wreck.
        MusicTrack from = SetTrackFixture.Track("a.mp3", structure: SetTrackFixture.StandardStructure());
        SongStructure twoDrops = SetTrackFixture.Structure(
            new SongSection(0.0, SongSectionLabel.Intro),
            new SongSection(30.0, SongSectionLabel.Drop),
            new SongSection(75.0, SongSectionLabel.Drop),
            new SongSection(240.0, SongSectionLabel.Outro));
        // The drums start at 60 s, so the entry is advanced past the first drop and the second one at 75 s
        // lands halfway through the 30 s blend.
        MusicTrack to = SetTrackFixture.Track("b.mp3", structure: twoDrops, kicks: KicksFrom(60.0, 300.0));

        TransitionShape? shape = SetTransitionPlanner.Plan(from, 0.0, to, Options, true, true);

        Assert.NotNull(shape);
        Assert.Equal(60.0, shape!.In.SourceSeconds, 3);
        Assert.Contains(SetWarning.IncomingDropInsideOverlap, shape.Warnings);
    }

    // A kick on every beat at 128 BPM, so the energy gate sees a driven floor and only the geometry is tested.
    private static double[] KicksFrom(double startSeconds, double endSeconds)
    {
        var kicks = new List<double>();
        for (double t = startSeconds; t < endSeconds; t += BarSeconds / 4.0)
            kicks.Add(t);
        return kicks.ToArray();
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
