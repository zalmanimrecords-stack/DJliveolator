using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Structure;
using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Studio.Set;

/// <summary>
/// Chooses where to leave one track and enter the next, and how long to hold them together. This is
/// where the musical judgement lives; the arranger below it only does geometry.
/// <para>Structure analysis is used but never trusted blindly. Section boundaries are heuristic, and a
/// confidently wrong one is worse than no structure at all — an "outro" mislabelled on a long reverb tail
/// throws away the back third of a record. So the structure is gated first, every candidate point is
/// checked against the track's own shape, and anything that fails falls back to the plain
/// distance-from-the-tail rule with a warning saying so.</para>
/// </summary>
public static class SetTransitionPlanner
{
    /// <summary>Below this many sections the segmentation carries no usable mix information.</summary>
    private const int MinTrustedSections = 3;

    /// <summary>A structure whose boundaries sit further than this from a bar line was computed against a
    /// different grid than the one we are mixing on, so it cannot be used for placement.</summary>
    private const double MaxBoundaryDriftBeats = 1.0;

    /// <summary>Mixing out earlier than this much of the way through throws away too much of the record;
    /// a structure point outside the window is treated as a mislabel.</summary>
    private const double EarliestMixOutFraction = 0.70;

    /// <summary>The incoming mix-in must have a kick within this many bars, or the blend starts over
    /// beatless material and the floor empties.</summary>
    private const double MaxBarsToFirstKick = 1.0;

    /// <summary>How far the entry may be pushed to reach the drums before the record is treated as unmixable
    /// rather than merely late. Measured: an unbounded advance entered a record at 270 s of a 300 s file, and
    /// the outgoing side then had no runway left — one bad kick-onset array turned a twelve-track set into a
    /// one-track set with innocent records blamed for being short.</summary>
    private const int MaxPhrasesToFirstKick = 2;

    private static readonly string[] StructureLabels =
        { SongSectionLabel.Drop, SongSectionLabel.BuildUp, SongSectionLabel.Breakdown };

    // A drop is the track's payload and a build-up promises one, so leaving on either is a broken promise;
    // an intro has nothing to leave from. A breakdown is the same broken promise measured from the other side:
    // on the 2026-08-13 set a breakdown mix-out recorded the outgoing record's own hole into the join —
    // 30.1 dB below its local level, 10.5 s of it bottoming at -63.7 dB — and reported that join as trusted.
    private static readonly string[] InvalidMixOutLabels =
    {
        SongSectionLabel.Drop, SongSectionLabel.BuildUp, SongSectionLabel.Intro, SongSectionLabel.Breakdown,
    };

    /// <summary>
    /// Plans the join from <paramref name="from"/> (already entered at <paramref name="fromMixInSeconds"/>)
    /// into <paramref name="to"/>. Returns <c>null</c> when even the shortest legal crossfade does not fit
    /// the runway either track has left — the caller drops the incoming track rather than mixing badly.
    /// </summary>
    /// <param name="fromPhaseReady">False when the outgoing track's grid failed the confidence gate.</param>
    /// <param name="toPhaseReady">False when the incoming track's grid failed the confidence gate.</param>
    public static TransitionShape? Plan(
        MusicTrack from,
        double fromMixInSeconds,
        MusicTrack to,
        SetBuildOptions options,
        bool fromPhaseReady,
        bool toPhaseReady)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(options);

        var warnings = new List<SetWarning>();
        MixAnchor inAnchor = PlanMixIn(to, warnings);
        if (DrumsStartTooLate(to, inAnchor))
            return null;

        // A guessed grid cannot be held in phase for long, so a low-confidence track gets the shortest
        // legal blend regardless of what was asked for.
        int requested = options.NormalizedOverlapBars;
        if (!fromPhaseReady || !toPhaseReady)
            requested = Math.Min(requested, SetBuildOptions.LowConfidenceOverlapBars);

        // Overlap and mix-out are mutually constrained (a longer blend needs more runway, which can push
        // the mix-out earlier than the record allows), so try the longest first and step down by a half
        // phrase until both ends fit.
        for (int bars = requested; bars >= SetBuildOptions.MinOverlapBars; bars -= SetBuildOptions.OverlapStepBars)
        {
            var attempt = new List<SetWarning>();
            MixAnchor? outAnchor = PlanMixOut(from, bars, fromMixInSeconds, attempt);
            if (outAnchor is null || !FitsIncomingRunway(to, inAnchor, bars))
                continue;

            // A shorter blend sits at a different point in each record, so a rejected anchor pair is a reason
            // to step down rather than to give up — the next attempt may clear the hole entirely.
            if (!KeepsTheFloorMoving(from, outAnchor, to, inAnchor, bars, attempt))
                continue;

            warnings.AddRange(attempt);
            // Against `requested`, not the option: when a low-confidence grid already capped the blend, eight
            // bars WAS the most allowed, and calling it a clamp reports a compromise nobody asked to avoid.
            if (bars < requested)
                warnings.Add(SetWarning.OverlapClamped);
            if (DropLandsInsideOverlap(to, inAnchor, bars))
                warnings.Add(SetWarning.IncomingDropInsideOverlap);

            return new TransitionShape(outAnchor, inAnchor, bars, Distinct(warnings));
        }

        return null;
    }

    /// <summary>
    /// Where to enter <paramref name="track"/>: its intro when the structure is trustworthy, otherwise its
    /// first downbeat — in both cases advanced to where the drums actually start, so a blend never opens
    /// over a beatless pad.
    /// </summary>
    public static MixAnchor PlanMixIn(MusicTrack track, ICollection<SetWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(warnings);

        PhraseGrid grid = PhraseGrid.For(track.Bpm);
        bool trusted = IsStructureTrusted(track, warnings);

        double raw = grid.DownbeatSeconds;
        string? label = null;
        if (trusted && FirstMixInSection(track.Structure!, grid) is { } section)
        {
            raw = section.StartSeconds;
            label = section.Label;
        }

        double anchor = grid.Nearest(raw);
        double adjusted = AdvanceToFirstKick(track, grid, anchor, warnings);

        // The advance moves the point off the section that chose it, but the section still chose the region.
        // Reporting Fallback here inverted the one trust signal the agent reads: every well-executed
        // long-blend entry read Fallback while a breakdown mix-out read Structure.
        return new MixAnchor(
            adjusted,
            adjusted > anchor ? null : label,
            trusted && label is not null ? AnchorSource.Structure : AnchorSource.Fallback);
    }

    /// <summary>
    /// Where to leave <paramref name="track"/> so that a <paramref name="overlapBars"/>-bar blend still
    /// fits before the file ends and no less than a phrase of the track has played. Null when it does not fit.
    /// </summary>
    public static MixAnchor? PlanMixOut(
        MusicTrack track,
        int overlapBars,
        double earliestSeconds,
        ICollection<SetWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(warnings);
        if (track.Duration is not { } duration)
            return null;

        PhraseGrid grid = PhraseGrid.For(track.Bpm);
        if (!grid.HasTempo)
            return null;

        double barSeconds = BarSeconds(track);
        double overlapSeconds = overlapBars * barSeconds;
        double latest = duration.TotalSeconds - overlapSeconds;
        double earliest = earliestSeconds + grid.PhraseSeconds;
        if (latest < earliest)
            return null;

        bool trusted = IsStructureTrusted(track, warnings);
        if (trusted)
        {
            if (StructureMixOut(track, grid, duration.TotalSeconds, earliest, latest) is { } fromStructure)
                return fromStructure;

            // Trustworthy segmentation that still offers no legal exit — a hard-ending record, or one
            // whose only late boundary is the drop itself.
            warnings.Add(SetWarning.StructureRejected);
        }

        // Fallback: leave a short tail after the blend rather than running the record to its last sample,
        // which is where hard endings and fade-outs live.
        double target = grid.Floor(latest - (SetBuildOptions.MinOverlapBars * barSeconds));

        // EarliestMixOutFraction used to be enforced only on the structure branch, and this is the branch that
        // runs for every record without trusted structure — measured cutting a 120 s record at 30 s. A tail is
        // a luxury; cutting a record at a quarter of its length is not, so give the tail up first. The
        // fraction is still not guaranteed: when the blend alone is half the record, no legal point reaches it.
        if (target < duration.TotalSeconds * EarliestMixOutFraction)
            target = grid.Floor(latest);

        if (target < earliest)
            target = grid.Ceiling(earliest);
        if (target > latest)
            target = grid.Floor(latest);

        return target < earliest || target > latest
            ? null
            : new MixAnchor(target, null, AnchorSource.Fallback);
    }

    /// <summary>
    /// Whether something is driving the floor at both ends of a join — the one condition the planner used to
    /// have no way of asking. False REJECTS the anchor pair rather than warning about it, because a warning
    /// nobody can act on is what let the 2026-08-13 set open a 10.5 s hole bottoming at -63.7 dB.
    /// <para>The condition is measured, not label-based: banning the Breakdown label alone would not have
    /// saved that set, because a gently-decaying breakdown gets labelled Section and the fallback mix-out path
    /// has no labels at all. Thresholds are <see cref="KickCoverage"/>'s (owner decision, 2026-08-28).</para>
    /// <para>A track whose kicks were never analyzed answers UNKNOWN and passes untouched: reading UNKNOWN as
    /// "no kicks" would make every un-analyzed record in the catalog unmixable.</para>
    /// </summary>
    public static bool KeepsTheFloorMoving(
        MusicTrack from,
        MixAnchor outAnchor,
        MusicTrack to,
        MixAnchor inAnchor,
        int overlapBars,
        ICollection<SetWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(outAnchor);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(inAnchor);
        ArgumentNullException.ThrowIfNull(warnings);

        // Both windows are inside their own files by construction — PlanMixOut keeps the whole blend before the
        // last sample, FitsIncomingRunway keeps a phrase after it — so neither can count bars past the end of a
        // track, which would read a legitimate last-phrase exit as a hole.
        if (KickCoverage.Fraction(from, outAnchor.SourceSeconds, overlapBars) is { } outCoverage &&
            outCoverage < KickCoverage.MixOutFloor)
            return false;

        if (KickCoverage.Fraction(to, inAnchor.SourceSeconds, overlapBars) is { } inCoverage &&
            inCoverage < KickCoverage.MixInFloor)
            warnings.Add(SetWarning.LowKickCoverageAtMixIn);

        // Strictly greater: MaxJointKicklessBars is the most that is ALLOWED, so >= would quietly move the
        // threshold to one bar. A null run means one deck was never measured, which cannot prove a hole.
        int? jointRun = KickCoverage.LongestJointKicklessRun(
            from, outAnchor.SourceSeconds, to, inAnchor.SourceSeconds, overlapBars);
        return jointRun is not > KickCoverage.MaxJointKicklessBars;
    }

    /// <summary>
    /// The trust gate on the segmentation itself. Adds <see cref="SetWarning.NoStructure"/> or
    /// <see cref="SetWarning.StructureRejected"/> when it cannot be used.
    /// </summary>
    public static bool IsStructureTrusted(MusicTrack track, ICollection<SetWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(warnings);

        SongStructure? structure = track.Structure;
        if (structure is null || structure.Sections.Count == 0)
        {
            warnings.Add(SetWarning.NoStructure);
            return false;
        }

        IReadOnlyList<SongSection> sections = structure.Ordered;
        if (sections.Count < MinTrustedSections || !sections.Any(s => StructureLabels.Contains(s.Label)))
            return Reject(warnings);

        // Boundaries far off the bar lines mean the segmentation was computed against a different grid
        // than the one we are about to mix on, so its positions cannot be used for placement.
        if (track.Bpm is not { Bpm: > 0.0 } bpm)
            return Reject(warnings);

        BeatGrid beats = BeatGrid.FromBpmResult(bpm);
        double[] drifts = sections
            .Select(s => Math.Abs(s.StartSeconds - beats.NearestDownbeatTo(s.StartSeconds)))
            .OrderBy(d => d)
            .ToArray();
        double median = drifts[drifts.Length / 2];
        if (median > MaxBoundaryDriftBeats * beats.BeatSeconds)
            return Reject(warnings);

        return true;
    }

    // The best point to leave on: the final outro when there is one after the last drop, else the last
    // section that is neither a drop nor a promise of one.
    private static MixAnchor? StructureMixOut(
        MusicTrack track,
        PhraseGrid grid,
        double durationSeconds,
        double earliest,
        double latest)
    {
        IReadOnlyList<SongSection> sections = track.Structure!.Ordered;
        double? lastDrop = sections
            .Where(s => s.Label == SongSectionLabel.Drop)
            .Select(s => (double?)s.StartSeconds)
            .LastOrDefault();

        // Never cut the record before its payload has landed and had a phrase to breathe.
        double afterPayload = lastDrop is { } drop ? drop + grid.PhraseSeconds : 0.0;
        double windowStart = Math.Max(earliest, Math.Max(afterPayload, durationSeconds * EarliestMixOutFraction));

        var candidates = sections
            .Where(s => !InvalidMixOutLabels.Contains(s.Label))
            .Select(s => new { s.Label, Snapped = grid.Floor(s.StartSeconds) })
            .Where(c => c.Snapped >= windowStart && c.Snapped <= latest)
            .ToList();
        if (candidates.Count == 0)
            return null;

        var chosen = candidates.LastOrDefault(c => c.Label == SongSectionLabel.Outro) ?? candidates[^1];
        return new MixAnchor(chosen.Snapped, chosen.Label, AnchorSource.Structure);
    }

    // The first section worth entering on: the intro, else whatever comes before the track first starts
    // building. Never a drop (an accidental double drop) and never an outro.
    private static SongSection? FirstMixInSection(SongStructure structure, PhraseGrid grid)
    {
        IReadOnlyList<SongSection> sections = structure.Ordered;

        // Only an intro at the HEAD of the record. The search used to run over the whole ordered list, so a
        // mid-track re-intro was taken as the entry point — measured at 150 s of a 300 s record, a random
        // mid-track entry into a by-definition low-energy section, reported as a trusted structural anchor.
        SongSection? intro = sections.FirstOrDefault(s => s.Label == SongSectionLabel.Intro);
        if (intro is not null && intro.StartSeconds <= grid.DownbeatSeconds + grid.PhraseSeconds)
            return intro;

        int firstEnergy = sections
            .Select((s, i) => (Section: s, Index: i))
            .Where(x => x.Section.Label is SongSectionLabel.BuildUp or SongSectionLabel.Drop)
            .Select(x => (int?)x.Index)
            .FirstOrDefault() ?? sections.Count;

        return firstEnergy > 0 ? sections[0] : null;
    }

    // Mixing into a beatless intro is the classic robot failure, and the kick onsets are already analyzed,
    // so move the entry to the phrase the drums actually start on.
    private static double AdvanceToFirstKick(
        MusicTrack track,
        PhraseGrid grid,
        double anchor,
        ICollection<SetWarning> warnings)
    {
        IReadOnlyList<double> kicks = track.Bpm?.KickOnsetsSeconds ?? Array.Empty<double>();
        if (kicks.Count == 0)
        {
            // Silence here made an unmeasured record indistinguishable from a perfect one.
            warnings.Add(SetWarning.KickOnsetsNotAnalyzed);
            return anchor;
        }

        double barSeconds = BarSeconds(track);
        double tolerance = MaxBarsToFirstKick * barSeconds;
        double? firstKick = kicks.Where(k => k >= anchor - tolerance).Select(k => (double?)k).FirstOrDefault();
        if (firstKick is null)
        {
            warnings.Add(SetWarning.NoKickAtMixIn);
            return anchor;
        }

        if (firstKick.Value <= anchor + tolerance)
            return anchor;

        // Ceiling, not Floor: snapping DOWN to a phrase line put the "corrected" entry up to a full phrase
        // BEFORE the kick, back inside the beatless material it was escaping — measured at 29 s — while the
        // warning claimed the entry had been fixed. The phrase line at or after the kick is the only snap that
        // both keeps the phrase-grid invariant and opens on the drums.
        warnings.Add(SetWarning.MixInMovedToKick);
        return Math.Max(anchor, grid.Ceiling(firstKick.Value));
    }

    // An entry pushed more than MaxPhrasesToFirstKick past the head is not a late entry, it is the wrong
    // record: there is no musical reading of a blend that opens two minutes into the incoming track.
    private static bool DrumsStartTooLate(MusicTrack to, MixAnchor inAnchor)
    {
        PhraseGrid grid = PhraseGrid.For(to.Bpm);
        return grid.HasTempo &&
            inAnchor.SourceSeconds > grid.DownbeatSeconds + (MaxPhrasesToFirstKick * grid.PhraseSeconds);
    }

    // The incoming track must have the blend plus a phrase of its own left after the mix-in point,
    // otherwise it is already ending as it arrives.
    private static bool FitsIncomingRunway(MusicTrack to, MixAnchor inAnchor, int overlapBars)
    {
        if (to.Duration is not { } duration)
            return false;

        PhraseGrid grid = PhraseGrid.For(to.Bpm);
        double needed = (overlapBars * BarSeconds(to)) + grid.PhraseSeconds;
        return inAnchor.SourceSeconds + needed <= duration.TotalSeconds;
    }

    // A drop landing while the previous record is still playing over it is a train wreck, so it is
    // reported even when the geometry is otherwise legal.
    private static bool DropLandsInsideOverlap(MusicTrack to, MixAnchor inAnchor, int overlapBars)
    {
        if (to.Structure is null)
            return false;

        // ANY drop in the window, not just the first. Testing only the first silently disabled the check
        // whenever the entry had been advanced past it — measured: entry 150 s, first drop 90 s, so the
        // function returned false while the drop at 165 s landed dead centre of the blend. The train-wreck
        // detector was off in exactly the configuration that produces train wrecks.
        double overlapEnd = inAnchor.SourceSeconds + (overlapBars * BarSeconds(to));
        return to.Structure.Ordered.Any(s =>
            s.Label == SongSectionLabel.Drop &&
            s.StartSeconds > inAnchor.SourceSeconds &&
            s.StartSeconds < overlapEnd);
    }

    /// <summary>One bar of <paramref name="track"/> in its own source seconds (0 when it has no tempo).</summary>
    internal static double BarSeconds(MusicTrack track)
        => track.Bpm is { Bpm: > 0.0 } bpm ? BeatGrid.FromBpmResult(bpm).BarSeconds : 0.0;

    // A structure that exists but cannot be believed. Reported distinctly from "no structure at all":
    // one says re-analyze, the other says the analysis is there but disagrees with the beat grid.
    private static bool Reject(ICollection<SetWarning> warnings)
    {
        warnings.Add(SetWarning.StructureRejected);
        return false;
    }

    private static IReadOnlyList<SetWarning> Distinct(List<SetWarning> warnings)
        => warnings.Count == 0 ? Array.Empty<SetWarning>() : warnings.Distinct().ToArray();
}
