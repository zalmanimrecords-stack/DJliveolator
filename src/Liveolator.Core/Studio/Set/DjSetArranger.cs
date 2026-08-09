using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Playlist;

namespace Liveolator.Core.Studio.Set;

/// <summary>
/// Builds a beat-matched DJ set: filters the pool to what can actually be mixed, orders it harmonically,
/// warps every track to one set tempo, and places each clip so its phrases land on the project's phrase
/// lines. The result is a <see cref="StudioProject"/> that opens in STUDIO and renders offline, plus a
/// report of every join and every track that did not make it.
/// <para>Pure and IO-free apart from the injectable reachability probe, so a whole set arranges in a unit
/// test with no audio and no files.</para>
/// <para><b>One tempo for the whole set.</b> The renderer samples a clip's warp factor once, at the clip's
/// start, so a tempo that moves inside a clip is silently not rendered — and two overlapping clips at
/// different rates drift apart within a bar. A single tempo is therefore the only shape that is both
/// renderable and phase-correct here. Sets that need to travel across tempo ranges want the stepped-tempo
/// model (tempo changes only at clip boundaries, with the boundary clip split in two), which is deliberately
/// not in this slice.</para>
/// </summary>
public sealed class DjSetArranger
{
    private readonly HarmonicSetBuilder _builder = new();
    private readonly Func<string, bool> _isReachable;

    /// <param name="isReachable">Whether a track's file can be decoded right now. Defaults to
    /// <see cref="TrackFileReachability.IsLocallyDecodable"/>; injected in tests so no files are needed.
    /// An unreachable file renders as silence with no error, so this gate is not optional.</param>
    public DjSetArranger(Func<string, bool>? isReachable = null)
        => _isReachable = isReachable ?? TrackFileReachability.IsLocallyDecodable;

    /// <summary>
    /// Builds a set from <paramref name="pool"/>. <paramref name="seed"/> starts the harmonic chain; when
    /// null the first eligible track is used. Returns an empty project (and the rejection list) when
    /// nothing in the pool can be mixed.
    /// </summary>
    public DjSetPlan Build(
        IReadOnlyList<MusicTrack> pool,
        MusicTrack? seed,
        HarmonicSetOptions harmonic,
        SetBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(harmonic);
        ArgumentNullException.ThrowIfNull(options);
        harmonic.Validate();
        options.Validate();

        var rejected = new List<RejectedCandidate>();
        List<MusicTrack> eligible = Eligible(pool, options, rejected);
        MusicTrack? start = seed is not null && eligible.Contains(seed) ? seed : eligible.FirstOrDefault();
        if (start is null)
            return Empty(options, rejected);

        IReadOnlyList<SetEntry> ordered = _builder.Build(start, eligible, harmonic).Entries;
        if (ordered.Count == 0)
            return Empty(options, rejected);

        // Tempo comes from the tracks actually chosen, not the whole library: the harmonic chain already
        // keeps consecutive tracks tempo-adjacent, so their median is a tempo most of them reach cheaply.
        double tempoBpm = MedianBpm(ordered.Select(e => e.Track));
        List<SetEntry> withinRange = WithinWarpLimit(ordered, tempoBpm, options, rejected);
        if (withinRange.Count == 0)
            return Empty(options, rejected);

        return Lay(withinRange, tempoBpm, options, rejected);
    }

    // Everything a track must have before it can be beat-matched at all. Cheap metadata checks run before
    // the reachability probe so an obviously unusable track never touches the filesystem.
    private List<MusicTrack> Eligible(IReadOnlyList<MusicTrack> pool, SetBuildOptions options, List<RejectedCandidate> rejected)
    {
        var eligible = new List<MusicTrack>();
        foreach (MusicTrack track in pool)
        {
            RejectReason? reason = Ineligible(track, options);
            if (reason is { } value)
                rejected.Add(new RejectedCandidate(track.File.Path, track.Title, value));
            else
                eligible.Add(track);
        }

        return eligible;
    }

    private RejectReason? Ineligible(MusicTrack track, SetBuildOptions options)
    {
        if (track.Status == MediaAnalysisStatus.Failed)
            return RejectReason.NotAnalyzed;
        if (track.Bpm is not { Bpm: > 0.0 })
            return RejectReason.NoBpm;
        if (track.Key is null)
            return RejectReason.NoKey;
        // An unknown length is not a missing nicety: a clip with no end sounds over everything after it.
        if (track.Duration is not { } duration)
            return RejectReason.NoDuration;
        if (duration.TotalSeconds < MinimumTrackSeconds(track))
            return RejectReason.TooShort;
        if (options.ExcludeLowGridConfidence && !GridConfidenceCalculator.Evaluate(track.Bpm).PhaseSyncReady)
            return RejectReason.LowGridConfidence;
        if (!_isReachable(track.File.Path))
            return RejectReason.FileUnreachable;

        return null;
    }

    // A track must hold a phrase before the blend, the shortest legal blend, and a phrase after it.
    private static double MinimumTrackSeconds(MusicTrack track)
    {
        PhraseGrid grid = PhraseGrid.For(track.Bpm);
        return (2 * grid.PhraseSeconds) + (SetBuildOptions.MinOverlapBars * SetTransitionPlanner.BarSeconds(track));
    }

    private static List<SetEntry> WithinWarpLimit(
        IReadOnlyList<SetEntry> ordered,
        double tempoBpm,
        SetBuildOptions options,
        List<RejectedCandidate> rejected)
    {
        var kept = new List<SetEntry>();
        foreach (SetEntry entry in ordered)
        {
            double warp = WarpPercent(tempoBpm, entry.Track.Bpm!.Bpm);
            if (Math.Abs(warp) > options.MaxWarpPercent)
            {
                rejected.Add(new RejectedCandidate(
                    entry.Track.File.Path, entry.Track.Title, RejectReason.OutsideTempoRange, Math.Round(warp, 2)));
                continue;
            }

            kept.Add(entry);
        }

        return kept;
    }

    // Places the chosen tracks. The invariant that makes the whole arrangement phase-correct: every clip's
    // SourceIn is one of its own phrase lines, and every clip starts on a project phrase line. Warping to a
    // common tempo maps a track's phrase onto the project's phrase exactly, so both hold by induction from
    // the first clip at t=0 — no per-transition correction, and every later phrase lands on the grid too.
    private DjSetPlan Lay(
        IReadOnlyList<SetEntry> entries,
        double tempoBpm,
        SetBuildOptions options,
        List<RejectedCandidate> rejected)
    {
        var clips = new List<StudioClip>();
        var transitions = new List<SetTransition>();
        var windows = new List<CrossfadeWindow>();

        MusicTrack current = entries[0].Track;
        var openingWarnings = new List<SetWarning>();
        double currentSourceIn = SetTransitionPlanner.PlanMixIn(current, openingWarnings).SourceSeconds;
        double currentStart = 0.0;
        bool currentWarped = IsPhaseReady(current);

        for (int i = 1; i < entries.Count; i++)
        {
            MusicTrack next = entries[i].Track;
            bool nextWarped = IsPhaseReady(next);
            TransitionShape? shape = SetTransitionPlanner.Plan(
                current, currentSourceIn, next, options, currentWarped, nextWarped);
            if (shape is null)
            {
                // Nothing left to mix out of, or the incoming record ends inside the blend. Dropping the
                // incoming track keeps the rest of the chain intact and is reported.
                rejected.Add(new RejectedCandidate(next.File.Path, next.Title, RejectReason.TooShort));
                continue;
            }

            double currentFactor = currentWarped ? tempoBpm / current.Bpm!.Bpm : 1.0;
            double outSourceOverlap = shape.OverlapBars * SetTransitionPlanner.BarSeconds(current);
            double outSourceEnd = shape.Out.SourceSeconds + outSourceOverlap;
            double blendStart = currentStart + ((shape.Out.SourceSeconds - currentSourceIn) / currentFactor);

            // An unwarped clip runs on its own bar length, so the chain loses the project grid across it.
            // Re-anchor the next clip to the project phrase grid to pick the alignment back up.
            if (!currentWarped)
                blendStart = SnapToProjectPhrase(blendStart, tempoBpm);

            clips.Add(Clip(current, clips.Count, options, currentStart, currentSourceIn, outSourceEnd, currentWarped));

            double blendSeconds = outSourceOverlap / currentFactor;
            int outSlot = SlotFor(clips.Count - 1, options);
            int inSlot = SlotFor(clips.Count, options);
            windows.Add(new CrossfadeWindow(outSlot, inSlot, blendStart, blendSeconds));
            transitions.Add(Report(
                transitions.Count, current, next, entries[i].Rationale, shape,
                blendStart, blendSeconds, tempoBpm, currentWarped, nextWarped));

            current = next;
            currentSourceIn = shape.In.SourceSeconds;
            currentStart = blendStart;
            currentWarped = nextWarped;
        }

        // The closing track plays out to the end of the file.
        clips.Add(Clip(current, clips.Count, options, currentStart, currentSourceIn, current.Duration!.Value.TotalSeconds, currentWarped));

        var project = new StudioProject(
            options.ProjectName, tempoBpm, clips, TransitionAutomation.Build(windows, tempoBpm));
        return new DjSetPlan(project, tempoBpm, transitions, rejected);
    }

    private static StudioClip Clip(
        MusicTrack track,
        int index,
        SetBuildOptions options,
        double timelineStart,
        double sourceIn,
        double sourceOut,
        bool warped)
    {
        BpmResult bpm = track.Bpm!;
        return new StudioClip(
            DeckSlot: SlotFor(index, options),
            TrackPath: track.File.Path,
            TimelineStartSeconds: timelineStart,
            SourceIn: TimeSpan.FromSeconds(sourceIn),
            SourceOut: TimeSpan.FromSeconds(Math.Min(sourceOut, track.Duration!.Value.TotalSeconds)),
            SourceBpm: bpm.Bpm,
            WarpEnabled: warped,
            Gain: 1.0,
            // Levels come entirely from the automation lanes: folding a linear clip fade into the
            // equal-power deck curve would put the −3 dB dip straight back in.
            FadeInSeconds: 0.0,
            FadeOutSeconds: 0.0,
            SourceDownbeatSeconds: bpm.DownbeatSeconds,
            SourceBeatsPerBar: bpm.BeatsPerBar);
    }

    private static SetTransition Report(
        int index,
        MusicTrack from,
        MusicTrack to,
        TransitionRationale? rationale,
        TransitionShape shape,
        double blendStart,
        double blendSeconds,
        double tempoBpm,
        bool fromWarped,
        bool toWarped)
    {
        GridConfidence fromGrid = GridConfidenceCalculator.Evaluate(from.Bpm);
        GridConfidence toGrid = GridConfidenceCalculator.Evaluate(to.Bpm);

        var warnings = shape.Warnings.ToList();
        if (!fromWarped || !toWarped)
            warnings.Add(SetWarning.LowGridConfidence);
        if (!fromGrid.Analyzed || !toGrid.Analyzed)
            warnings.Add(SetWarning.GridNotAnalyzed);

        return new SetTransition(
            Index: index,
            FromPath: from.File.Path,
            ToPath: to.File.Path,
            StartSeconds: Math.Round(blendStart, 3),
            EndSeconds: Math.Round(blendStart + blendSeconds, 3),
            OverlapBars: shape.OverlapBars,
            OverlapSeconds: Math.Round(blendSeconds, 3),
            Type: shape.OverlapBars > SetBuildOptions.MinOverlapBars ? TransitionType.Blend : TransitionType.Short,
            OutAnchor: shape.Out,
            InAnchor: shape.In,
            TempoBpm: tempoBpm,
            FromWarpPercent: fromWarped ? Math.Round(WarpPercent(tempoBpm, from.Bpm!.Bpm), 2) : 0.0,
            ToWarpPercent: toWarped ? Math.Round(WarpPercent(tempoBpm, to.Bpm!.Bpm), 2) : 0.0,
            KeyFrom: from.Key?.Camelot,
            KeyTo: to.Key?.Camelot,
            KeyRelationship: rationale?.Relationship,
            PhaseLocked: fromWarped && toWarped,
            GridConfidenceFrom: fromGrid.Display,
            GridConfidenceTo: toGrid.Display,
            Warnings: warnings.Distinct().ToArray());
    }

    /// <summary>
    /// A track only warps when its grid can be trusted. Stretching by a ratio derived from a guessed tempo
    /// is wrong twice over — the wrong amount, and no way to verify the result — so a low-confidence track
    /// plays at its native rate and takes the shortest blend instead.
    /// </summary>
    private static bool IsPhaseReady(MusicTrack track)
        => GridConfidenceCalculator.Evaluate(track.Bpm).PhaseSyncReady;

    private static double WarpPercent(double tempoBpm, double sourceBpm)
        => ((tempoBpm / sourceBpm) - 1.0) * 100.0;

    private static int SlotFor(int index, SetBuildOptions options)
        => (options.StartDeckSlot + index) % 2;

    private static double SnapToProjectPhrase(double seconds, double tempoBpm)
    {
        double phrase = SetBuildOptions.PhraseBars * SetBuildOptions.BarSeconds(tempoBpm);
        return phrase > 0.0 ? Math.Max(0.0, Math.Round(seconds / phrase) * phrase) : seconds;
    }

    private static double MedianBpm(IEnumerable<MusicTrack> tracks)
    {
        double[] values = tracks.Select(t => t.Bpm!.Bpm).OrderBy(b => b).ToArray();
        int middle = values.Length / 2;
        double median = values.Length % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2.0;
        return Math.Round(median, 1);
    }

    private static DjSetPlan Empty(SetBuildOptions options, IReadOnlyList<RejectedCandidate> rejected)
        => new(StudioProject.Empty(options.ProjectName), StudioProject.DefaultBpm, Array.Empty<SetTransition>(), rejected);
}
