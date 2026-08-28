using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Structure;
using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Studio.Set;

/// <summary>What one join was found to be compromised by, re-derived rather than remembered.</summary>
public enum SetJoinFinding
{
    /// <summary>A clip's track is no longer in the catalog, so this join cannot be judged at all. Reported
    /// instead of a clean result: silence about an unknown join is the lie this whole audit exists to stop.</summary>
    Unverifiable,

    /// <summary>One side's grid failed <see cref="GridConfidence.PhaseSyncReady"/>, so it is not phase-locked.</summary>
    LowGridConfidence,

    /// <summary>One side predates grid-confidence analysis, so its grid quality is unknown.</summary>
    GridNotAnalyzed,

    /// <summary>One side has no structure analysis, so its mix point came from the fallback rule.</summary>
    NoStructure,

    /// <summary>One side's structure exists but failed the trust checks.</summary>
    StructureRejected,

    /// <summary>The incoming window is under <see cref="KickCoverage.MixInFloor"/> — the blend opens over
    /// material with barely any kick in it.</summary>
    KicklessMixIn,

    /// <summary>A drop of the incoming track lands inside the blend, with the outgoing record still playing
    /// over it.</summary>
    DropInsideOverlap,

    /// <summary>More than <see cref="KickCoverage.MaxJointKicklessBars"/> consecutive bars inside the blend
    /// have no kick on EITHER deck. The mix has a hole there.</summary>
    JointKicklessRun,
}

/// <summary>
/// One join's geometry, in the terms the audit needs: where each side's playhead sits when the blend starts,
/// how long the blend is, and whether each clip was warped onto the set tempo. Plain data on purpose — the
/// caller is <c>Liveolator.Mcp</c> reading a saved <c>StudioProject</c>, and Core must not know that.
/// </summary>
/// <param name="MixOutSourceSeconds">Where the outgoing track is, in its own source seconds, at the blend start.</param>
/// <param name="MixInSourceSeconds">The incoming clip's source-in, in its own source seconds.</param>
/// <param name="OverlapBars">Blend length in bars of the SET tempo.</param>
/// <param name="SetTempoBpm">The one tempo the set plays at.</param>
/// <param name="OutgoingWarped">False when the outgoing clip runs at its native tempo.</param>
/// <param name="IncomingWarped">False when the incoming clip runs at its native tempo.</param>
public sealed record SetJoinGeometry(
    double MixOutSourceSeconds,
    double MixInSourceSeconds,
    int OverlapBars,
    double SetTempoBpm,
    bool OutgoingWarped,
    bool IncomingWarped);

/// <summary>
/// What the audit found, plus the numbers behind it so a caller can report the measurement rather than only
/// the verdict. A coverage of <c>null</c> means unknown (the kicks were never analyzed), never zero.
/// </summary>
public sealed record SetJoinAuditResult(
    IReadOnlyList<SetJoinFinding> Findings,
    double? MixOutKickCoverage,
    double? MixInKickCoverage,
    int? JointKicklessBars);

/// <summary>
/// Re-derives a join's quality findings from the two catalogued tracks and the join's geometry alone.
/// <para>Why re-derive instead of storing what the planner knew: a stored report goes stale the instant
/// STUDIO moves a clip, at which point it vouches for an arrangement that no longer exists — and the export
/// path already holds the full catalog one line before it judges the set. This also makes every set saved
/// before the audit existed judgeable, which a sidecar store could never do.</para>
/// <para>Pure and synchronous: nothing here decodes audio or touches a file.</para>
/// </summary>
public static class SetJoinAudit
{
    /// <summary>
    /// Audits one join. A null track means its clip's file is no longer in the catalog, which yields
    /// <see cref="SetJoinFinding.Unverifiable"/> alone — nothing else about the join can be believed.
    /// </summary>
    public static SetJoinAuditResult Audit(MusicTrack? outgoing, MusicTrack? incoming, SetJoinGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (outgoing is null || incoming is null)
            return new SetJoinAuditResult(new[] { SetJoinFinding.Unverifiable }, null, null, null);

        var findings = new List<SetJoinFinding>();
        AddGridFindings(outgoing, findings);
        AddGridFindings(incoming, findings);
        AddStructureFindings(outgoing, incoming, findings);

        int outBars = WindowBars(outgoing, geometry.OutgoingWarped, geometry);
        int inBars = WindowBars(incoming, geometry.IncomingWarped, geometry);
        double? outCoverage = KickCoverage.Fraction(outgoing, geometry.MixOutSourceSeconds, outBars);
        double? inCoverage = KickCoverage.Fraction(incoming, geometry.MixInSourceSeconds, inBars);

        // The joint run is only defined where both windows overlap in bars, and beyond the shorter one there
        // is no join left to judge. Bars of unequal length (one side unwarped) make this approximate by up to
        // one bar; the alternative is a second time base for a case that only arises on an unwarped clip.
        int? jointKickless = KickCoverage.LongestJointKicklessRun(
            outgoing, geometry.MixOutSourceSeconds,
            incoming, geometry.MixInSourceSeconds,
            Math.Min(outBars, inBars));

        if (inCoverage < KickCoverage.MixInFloor)
            findings.Add(SetJoinFinding.KicklessMixIn);
        if (jointKickless > KickCoverage.MaxJointKicklessBars)
            findings.Add(SetJoinFinding.JointKicklessRun);
        if (DropLandsInsideOverlap(incoming, geometry.MixInSourceSeconds, inBars))
            findings.Add(SetJoinFinding.DropInsideOverlap);

        return new SetJoinAuditResult(findings.Distinct().ToArray(), outCoverage, inCoverage, jointKickless);
    }

    private static void AddGridFindings(MusicTrack track, ICollection<SetJoinFinding> findings)
    {
        GridConfidence grid = GridConfidenceCalculator.Evaluate(track.Bpm);
        if (!grid.Analyzed)
            findings.Add(SetJoinFinding.GridNotAnalyzed);
        else if (!grid.PhaseSyncReady)
            findings.Add(SetJoinFinding.LowGridConfidence);
    }

    // Reuses the planner's own trust gate, so the audit cannot disagree with the decision that produced the
    // arrangement. The warnings it emits are per-track; the findings are per-join, so both sides collapse
    // into one — the join is named by its caller, which is where the two file names already live.
    private static void AddStructureFindings(MusicTrack outgoing, MusicTrack incoming, ICollection<SetJoinFinding> findings)
    {
        var warnings = new List<SetWarning>();
        SetTransitionPlanner.IsStructureTrusted(outgoing, warnings);
        SetTransitionPlanner.IsStructureTrusted(incoming, warnings);

        if (warnings.Contains(SetWarning.NoStructure))
            findings.Add(SetJoinFinding.NoStructure);
        if (warnings.Contains(SetWarning.StructureRejected))
            findings.Add(SetJoinFinding.StructureRejected);
    }

    // How many of the track's OWN bars the blend covers. A warped clip is stretched onto the set grid, so its
    // bars are the set's bars; an unwarped one keeps its native bar length, and measuring it at the set's bar
    // count is the error that reports a fast unwarped clip's blend as shorter than it is.
    // Either bar length missing falls back to the requested count, never to a shorter window: a one-bar
    // window is 100% covered by a single strike, so shrinking on bad input would buy a passing audit.
    private static int WindowBars(MusicTrack track, bool warped, SetJoinGeometry geometry)
    {
        double ownBarSeconds = SetTransitionPlanner.BarSeconds(track);
        double setBarSeconds = SetBuildOptions.BarSeconds(geometry.SetTempoBpm);
        if (warped || ownBarSeconds <= 0.0 || setBarSeconds <= 0.0)
            return Math.Max(1, geometry.OverlapBars);

        return Math.Max(1, (int)Math.Round(geometry.OverlapBars * setBarSeconds / ownBarSeconds));
    }

    // ANY drop inside the blend, not just the first: testing only the first silently disables the check
    // whenever the entry was pushed past that drop, which is precisely the configuration that train-wrecks.
    private static bool DropLandsInsideOverlap(MusicTrack incoming, double mixInSeconds, int bars)
    {
        if (incoming.Structure is null)
            return false;

        double overlapEnd = mixInSeconds + (bars * SetTransitionPlanner.BarSeconds(incoming));
        return incoming.Structure.Ordered.Any(s =>
            s.Label == SongSectionLabel.Drop && s.StartSeconds > mixInSeconds && s.StartSeconds < overlapEnd);
    }
}
