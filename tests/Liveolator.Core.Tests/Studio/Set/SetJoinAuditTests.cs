using Liveolator.Core.Analysis.Structure;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Studio.Set;

namespace Liveolator.Core.Tests.Studio.Set;

/// <summary>
/// Re-deriving a join's quality from the catalog and the join geometry alone — what lets the export gate
/// judge an already-saved set (including one edited in STUDIO since it was built) with nothing stored.
/// </summary>
public class SetJoinAuditTests
{
    private const double BarSeconds = 1.875;    // 128 BPM
    private const double BeatSeconds = 0.46875;

    /// <summary>A 16-bar blend at the set tempo, both clips warped onto it — the ordinary case.</summary>
    private static SetJoinGeometry Geometry(double mixOutSeconds = 0.0, double mixInSeconds = 0.0)
        => new(mixOutSeconds, mixInSeconds, OverlapBars: 16, SetTempoBpm: 128.0,
               OutgoingWarped: true, IncomingWarped: true);

    private static double[] KicksEveryBeat(double fromSeconds, int count, double beatSeconds = BeatSeconds)
        => Enumerable.Range(0, count).Select(i => fromSeconds + (i * beatSeconds)).ToArray();

    /// <summary>Kicks all the way through, so this side never contributes a hole.</summary>
    private static MusicTrack Driving(string path, string camelot = "8A")
        => SetTrackFixture.Track(path, camelot, kicks: KicksEveryBeat(0.0, 640), structure: SetTrackFixture.StandardStructure());

    [Fact]
    public void Audit_ReportsLowGridConfidence()
    {
        SetJoinAuditResult audit = SetJoinAudit.Audit(
            SetTrackFixture.UntrustedGrid("out.mp3"), Driving("in.mp3"), Geometry());

        Assert.Contains(SetJoinFinding.LowGridConfidence, audit.Findings);
    }

    [Fact]
    public void Audit_ReportsAGridThatWasNeverAnalyzed()
    {
        SetJoinAuditResult audit = SetJoinAudit.Audit(
            Driving("out.mp3"), SetTrackFixture.UnanalyzedGrid("in.mp3"), Geometry());

        Assert.Contains(SetJoinFinding.GridNotAnalyzed, audit.Findings);
        Assert.DoesNotContain(SetJoinFinding.LowGridConfidence, audit.Findings);
    }

    [Fact]
    public void Audit_ReportsAMissingStructure()
    {
        SetJoinAuditResult audit = SetJoinAudit.Audit(
            SetTrackFixture.Track("out.mp3"), Driving("in.mp3"), Geometry());

        Assert.Contains(SetJoinFinding.NoStructure, audit.Findings);
    }

    [Fact]
    public void Audit_ReportsARejectedStructure()
    {
        SongStructure thin = SetTrackFixture.Structure(
            new SongSection(0.0, SongSectionLabel.Intro),
            new SongSection(90.0, SongSectionLabel.Drop));
        MusicTrack outgoing = SetTrackFixture.Track("out.mp3", structure: thin, kicks: KicksEveryBeat(0.0, 640));

        SetJoinAuditResult audit = SetJoinAudit.Audit(outgoing, Driving("in.mp3"), Geometry());

        Assert.Contains(SetJoinFinding.StructureRejected, audit.Findings);
    }

    [Fact]
    public void Audit_ReportsAKicklessMixIn()
    {
        // The incoming record's drums stop 5 s in, so the blend opens over its pad.
        MusicTrack incoming = SetTrackFixture.Track("in.mp3", kicks: KicksEveryBeat(0.0, 10));

        SetJoinAuditResult audit = SetJoinAudit.Audit(Driving("out.mp3"), incoming, Geometry());

        Assert.Contains(SetJoinFinding.KicklessMixIn, audit.Findings);
        Assert.NotNull(audit.MixInKickCoverage);
        Assert.True(audit.MixInKickCoverage < KickCoverage.MixInFloor);
    }

    [Fact]
    public void Audit_SaysNothingAboutTheMixIn_WhenItIsFullyCovered()
    {
        SetJoinAuditResult audit = SetJoinAudit.Audit(Driving("out.mp3"), Driving("in.mp3"), Geometry());

        Assert.DoesNotContain(SetJoinFinding.KicklessMixIn, audit.Findings);
        Assert.DoesNotContain(SetJoinFinding.JointKicklessRun, audit.Findings);
        Assert.Equal(1.0, audit.MixInKickCoverage);
        Assert.Equal(1.0, audit.MixOutKickCoverage);
        Assert.Equal(0, audit.JointKicklessBars);
    }

    [Fact]
    public void Audit_ReportsADropInsideTheOverlap()
    {
        // The drop lands 15 s into a 30 s blend, with the outgoing record still playing over it.
        SongStructure structure = SetTrackFixture.Structure(
            new SongSection(0.0, SongSectionLabel.Intro),
            new SongSection(15.0, SongSectionLabel.Drop),
            new SongSection(120.0, SongSectionLabel.Breakdown),
            new SongSection(240.0, SongSectionLabel.Outro));
        MusicTrack incoming = SetTrackFixture.Track("in.mp3", structure: structure, kicks: KicksEveryBeat(0.0, 640));

        SetJoinAuditResult audit = SetJoinAudit.Audit(Driving("out.mp3"), incoming, Geometry());

        Assert.Contains(SetJoinFinding.DropInsideOverlap, audit.Findings);
    }

    [Fact]
    public void Audit_ReportsADropInsideTheOverlap_EvenWhenItIsNotTheFirstOne()
    {
        // The measured defect: checking only the FIRST drop disables the train-wreck detector in exactly
        // the configuration that produces train wrecks (an entry pushed past that drop).
        SongStructure structure = SetTrackFixture.Structure(
            new SongSection(0.0, SongSectionLabel.Intro),
            new SongSection(30.0, SongSectionLabel.Drop),
            new SongSection(90.0, SongSectionLabel.Breakdown),
            new SongSection(105.0, SongSectionLabel.Drop),
            new SongSection(240.0, SongSectionLabel.Outro));
        MusicTrack incoming = SetTrackFixture.Track("in.mp3", structure: structure, kicks: KicksEveryBeat(0.0, 640));

        SetJoinAuditResult audit = SetJoinAudit.Audit(
            Driving("out.mp3"), incoming, Geometry(mixInSeconds: 90.0));

        Assert.Contains(SetJoinFinding.DropInsideOverlap, audit.Findings);
    }

    [Fact]
    public void Audit_ReportsAJointKicklessRun()
    {
        // Both records withdraw together for four bars — the 2026-08-13 hole.
        MusicTrack outgoing = SetTrackFixture.Track("out.mp3", kicks: KicksEveryBeat(0.0, 8));
        MusicTrack incoming = SetTrackFixture.Track("in.mp3", kicks: KicksEveryBeat(6 * BarSeconds, 64));

        SetJoinAuditResult audit = SetJoinAudit.Audit(outgoing, incoming, Geometry());

        Assert.Contains(SetJoinFinding.JointKicklessRun, audit.Findings);
        Assert.Equal(4, audit.JointKicklessBars);
    }

    [Fact]
    public void Audit_SaysNothingAboutEnergy_WhenTheKicksWereNeverAnalyzed()
    {
        // Backward compatibility: an un-analyzed record is unknown, never a hole.
        SetJoinAuditResult audit = SetJoinAudit.Audit(
            SetTrackFixture.Track("out.mp3"), SetTrackFixture.Track("in.mp3"), Geometry());

        Assert.DoesNotContain(SetJoinFinding.KicklessMixIn, audit.Findings);
        Assert.DoesNotContain(SetJoinFinding.JointKicklessRun, audit.Findings);
        Assert.Null(audit.MixInKickCoverage);
        Assert.Null(audit.JointKicklessBars);
    }

    [Fact]
    public void Audit_MeasuresTheRealBarCount_WhenAnUnwarpedClipIsFaster()
    {
        // An unwarped 160 BPM clip runs its own bars 25% shorter than the set's, so a 16-bar blend at
        // 128 BPM covers 20 of ITS bars. Measuring it at 16 would miss the last four bars of the window.
        MusicTrack incoming = SetTrackFixture.Track(
            "in.mp3", bpm: 160.0,
            kicks: KicksEveryBeat(0.0, 4 * 16, beatSeconds: 0.375));   // 16 of its OWN bars, then silence
        var geometry = new SetJoinGeometry(
            0.0, 0.0, OverlapBars: 16, SetTempoBpm: 128.0, OutgoingWarped: true, IncomingWarped: false);

        SetJoinAuditResult audit = SetJoinAudit.Audit(Driving("out.mp3"), incoming, geometry);

        Assert.Equal(16.0 / 20.0, audit.MixInKickCoverage!.Value, precision: 6);
    }

    [Fact]
    public void Audit_MeasuresTheSetsBarCount_WhenTheSetTempoIsUnusable()
    {
        // A window that collapses to one bar reads as clean, because one bar with one kick is 100% covered.
        // A malformed project must not be able to buy a passing audit, so an unusable set tempo falls back
        // to the requested bar count rather than to a window short enough to hide the hole.
        MusicTrack incoming = SetTrackFixture.Track("in.mp3", kicks: KicksEveryBeat(0.0, 4));
        var geometry = new SetJoinGeometry(
            0.0, 0.0, OverlapBars: 16, SetTempoBpm: 0.0, OutgoingWarped: true, IncomingWarped: false);

        SetJoinAuditResult audit = SetJoinAudit.Audit(Driving("out.mp3"), incoming, geometry);

        Assert.Equal(1.0 / 16.0, audit.MixInKickCoverage!.Value, precision: 6);
        Assert.Contains(SetJoinFinding.KicklessMixIn, audit.Findings);
    }

    [Fact]
    public void Audit_SaysUnverifiable_WhenTheTrackIsNotInTheCatalog()
    {
        SetJoinAuditResult audit = SetJoinAudit.Audit(Driving("out.mp3"), incoming: null, Geometry());

        Assert.Equal(new[] { SetJoinFinding.Unverifiable }, audit.Findings);
        Assert.Null(audit.MixInKickCoverage);
        Assert.Null(audit.MixOutKickCoverage);
        Assert.Null(audit.JointKicklessBars);
    }
}
