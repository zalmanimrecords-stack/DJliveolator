using System.Text.Json;
using Liveolator.Audio.Render;
using Liveolator.Core.Analysis;
using Liveolator.Core.Dsp;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Playlist;
using Liveolator.Core.Studio;
using Liveolator.Core.Studio.Set;
using Liveolator.Mcp.Contracts;
using Microsoft.Extensions.Logging;

namespace Liveolator.Mcp.Session;

/// <summary>
/// Builds, reads and auditions DJ sets for the MCP tools: pulls the catalog from
/// <see cref="LibrarySession"/>, runs the Core arranger, persists the arrangement where the app's STUDIO
/// tab will find it, and renders transition previews. A thin adapter — every decision about how a set is
/// mixed lives in <see cref="DjSetArranger"/>.
/// </summary>
public sealed class DjSetSession
{
    /// <summary>How much of the surrounding set a transition preview includes on each side.</summary>
    private const int PreviewLeadBars = SetBuildOptions.PhraseBars;

    /// <summary>Enough rejections for the caller to see the pattern without flooding its context.</summary>
    private const int MaxReportedRejections = 15;

    private readonly LibrarySession _library;
    private readonly IStudioProjectStore _store;
    private readonly OfflineMixRenderer _renderer;
    private readonly ILoudnessMeter _loudnessMeter;
    private readonly ILogger<DjSetSession> _logger;

    public DjSetSession(
        LibrarySession library,
        IStudioProjectStore store,
        OfflineMixRenderer renderer,
        ILoudnessMeter loudnessMeter,
        ILogger<DjSetSession> logger)
    {
        _library = library;
        _store = store;
        _renderer = renderer;
        _loudnessMeter = loudnessMeter;
        _logger = logger;
    }

    /// <summary>
    /// Builds a set from the catalog and saves it under <see cref="SetBuildOptions.ProjectName"/>.
    /// <para><paramref name="trackPaths"/> narrows the candidate pool to exactly those catalogued tracks.
    /// Without it the pool is the whole catalog, and a second unrelated library in the same data root
    /// competes on the only signals the arranger has (tempo and key), so it wins joins no tolerance
    /// setting can deny it.</para>
    /// </summary>
    /// <exception cref="ArgumentException">The seed or a named candidate is not catalogued, or the seed
    /// is not among the named candidates.</exception>
    public async Task<DjSetResult> BuildAsync(
        string? seedPath,
        HarmonicSetOptions harmonic,
        SetBuildOptions options,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? trackPaths = null)
    {
        ArgumentNullException.ThrowIfNull(harmonic);
        ArgumentNullException.ThrowIfNull(options);

        MusicTrack? seed = null;
        if (!string.IsNullOrWhiteSpace(seedPath))
        {
            seed = await _library.GetAsync(seedPath, cancellationToken).ConfigureAwait(false);
            if (seed is null)
                throw new ArgumentException($"No catalogued track at '{seedPath}'. Scan its folder first, or check the path.");
        }

        IReadOnlyList<MusicTrack> pool = trackPaths is { Count: > 0 }
            ? await RestrictAsync(trackPaths, seed, cancellationToken).ConfigureAwait(false)
            : await _library.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        DjSetPlan plan = new DjSetArranger().Build(pool, seed, harmonic, options);

        try
        {
            await _store.SaveAsync(plan.Project, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not save the set {Name}", plan.Project.Name);
            throw new InvalidOperationException($"Built the set but could not save it: {ex.Message}", ex);
        }

        _logger.LogInformation(
            "Built set {Name}: {Tracks} tracks at {Bpm} BPM, {Rejected} candidates rejected",
            plan.Project.Name, plan.TrackCount, plan.TempoBpm, plan.Rejected.Count);

        return Describe(plan, ByPath(pool));
    }

    /// <summary>
    /// The candidate pool narrowed to exactly <paramref name="trackPaths"/>. Each path is resolved through
    /// the catalog's path-or-name lookup (doc 31 L5) so an agent's differently-spelled path still matches,
    /// and an unresolvable one is reported rather than silently dropped — a set quietly built from fewer
    /// records than were asked for is the failure this parameter exists to prevent.
    /// </summary>
    private async Task<IReadOnlyList<MusicTrack>> RestrictAsync(
        IReadOnlyList<string> trackPaths,
        MusicTrack? seed,
        CancellationToken cancellationToken)
    {
        var restricted = new List<MusicTrack>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in trackPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Candidate track paths cannot include a blank entry.", nameof(trackPaths));

            MusicTrack track = await _library.GetAsync(path, cancellationToken).ConfigureAwait(false)
                ?? throw new ArgumentException(
                    $"No catalogued track at '{path}'. Scan its folder first, or check the path.", nameof(trackPaths));
            if (seen.Add(track.File.Path))
                restricted.Add(track);
        }

        if (seed is not null && !seen.Contains(seed.File.Path))
            throw new ArgumentException(
                $"The seed '{seed.File.Path}' is not among the candidate tracks. Add it to trackPaths, or seed from one of them.",
                nameof(trackPaths));

        return restricted;
    }

    /// <summary>The names of every saved set, in the store's stable order.</summary>
    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
        => _store.ListAsync(cancellationToken);

    /// <summary>A saved set as it can be read back, or null when no set is stored under that name.</summary>
    public async Task<SavedSetInfo?> GetAsync(string name, CancellationToken cancellationToken)
    {
        StudioProject? project = await _store.LoadAsync(name, cancellationToken).ConfigureAwait(false);
        if (project is null)
            return null;

        IReadOnlyList<MusicTrack> pool = await _library.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, MusicTrack> byPath = ByPath(pool);

        return new SavedSetInfo(
            project.Name,
            project.Clips.Count,
            Math.Round(project.DurationSeconds, 1),
            project.Bpm,
            project.Clips.Select((clip, i) => Track(i, clip, byPath, project.Bpm)).ToArray(),
            Joins(project).Select((join, i) => new SetJoinInfo(
                i, join.FromPath, join.ToPath,
                Math.Round(join.StartSeconds, 3),
                Math.Round(join.EndSeconds, 3),
                Math.Round(join.EndSeconds - join.StartSeconds, 3),
                Math.Round((join.EndSeconds - join.StartSeconds) / SetBuildOptions.BarSeconds(project.Bpm), 2)))
                .ToArray());
    }

    /// <summary>
    /// Renders each of the set's transitions to its own WAV, with a phrase of lead-in and lead-out.
    /// <para>Only the joins are rendered because only the joins need judging — and because rendering a
    /// full set decodes every track and holds the whole master in memory at once, which an hour-long mix
    /// does not survive.</para>
    /// </summary>
    /// <exception cref="ArgumentException">No set is saved under that name.</exception>
    public async Task<SetPreviewResult> RenderPreviewAsync(
        string name,
        string outputDirectory,
        int sampleRate,
        CancellationToken cancellationToken)
    {
        StudioProject? project = await _store.LoadAsync(name, cancellationToken).ConfigureAwait(false);
        if (project is null)
            throw new ArgumentException($"No saved set named '{name}'. Build one first, or list the saved sets.");

        Directory.CreateDirectory(outputDirectory);
        double lead = PreviewLeadBars * SetBuildOptions.BarSeconds(project.Bpm);
        double total = project.DurationSeconds;

        var clips = new List<SetPreviewClip>();
        IReadOnlyList<Join> joins = Joins(project);
        for (int i = 0; i < joins.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Join join = joins[i];
            double start = Math.Max(0.0, join.StartSeconds - lead);
            double end = Math.Min(total, join.EndSeconds + lead);
            StudioProject slice = ProjectSlice.Extract(project, start, end, $"{project.Name} — transition {i + 1}");
            if (slice.Clips.Count == 0)
                continue;

            string outputPath = Path.Combine(outputDirectory, PreviewFileName(name, i, join));
            try
            {
                await _renderer.RenderAsync(slice, outputPath, sampleRate, progress: null, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                // One unreadable source must not lose the previews that did render.
                _logger.LogError(ex, "Could not render transition {Index} of {Name}", i, name);
                continue;
            }

            clips.Add(new SetPreviewClip(
                i, outputPath, Math.Round(start, 3), Math.Round(end - start, 3), join.FromPath, join.ToPath));
        }

        return new SetPreviewResult(
            project.Name, outputDirectory, clips.Count,
            Math.Round(clips.Sum(c => c.DurationSeconds), 1), clips);
    }

    /// <summary>
    /// Renders a saved set to ONE continuous mix, plus the tracklist artifacts a publish needs.
    /// <para>Refuses by default when the mix is not fit to publish and returns the reasons and remedies
    /// instead — a level-stepping, drifting mix is worse than no file. <paramref name="force"/> renders
    /// anyway, for when the owner has listened and decided.</para>
    /// <para>Unreachable source files always fail, force or not: this catalog lives partly on a network
    /// share, and a share that drops mid-render would otherwise produce a long mix with silent stretches
    /// that nothing reports.</para>
    /// </summary>
    /// <exception cref="ArgumentException">No set is saved under that name, or a source file is missing.</exception>
    public async Task<SetMixExport> ExportMixAsync(
        string name,
        string outputDirectory,
        int sampleRate,
        bool force,
        CancellationToken cancellationToken)
    {
        StudioProject? project = await _store.LoadAsync(name, cancellationToken).ConfigureAwait(false);
        if (project is null)
            throw new ArgumentException($"No saved set named '{name}'. Build one first, or list the saved sets.");
        if (project.Clips.Count == 0)
            throw new ArgumentException($"The set '{name}' has no clips to render.");

        Directory.CreateDirectory(outputDirectory);

        // Reachability first: cheaper to fail now than 70 minutes into a render.
        string[] missing = project.Clips
            .Select(c => c.TrackPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(p => !File.Exists(p))
            .ToArray();
        if (missing.Length > 0)
            throw new ArgumentException(
                $"{missing.Length} source file(s) are not reachable, so the mix would contain silent " +
                $"stretches. Check the drive holding: {string.Join(", ", missing.Take(3))}" +
                (missing.Length > 3 ? $" (+{missing.Length - 3} more)" : string.Empty));

        IReadOnlyList<MusicTrack> catalog = await _library.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<MixTrackEntry> tracks = Tracklist(project, catalog);
        IReadOnlyList<MixGateIssue> issues = PublishGate(project);

        if (issues.Count > 0 && !force)
        {
            _logger.LogInformation(
                "Refused to export '{Name}': {Count} publish-gate issue(s)", name, issues.Count);
            return new SetMixExport(
                Rendered: false, AudioPath: null, TracklistPath: null, ChaptersPath: null,
                DurationSeconds: Math.Round(project.DurationSeconds, 1),
                IntegratedLufs: null, CeilingDbTp: LimiterSettings.Default.CeilingDbTp,
                Issues: issues, Tracks: tracks);
        }

        string safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        string audioPath = Path.Combine(outputDirectory, $"{safeName}.wav");
        await _renderer.RenderAsync(project, audioPath, sampleRate, progress: null, cancellationToken)
            .ConfigureAwait(false);

        string tracklistPath = Path.Combine(outputDirectory, $"{safeName}-tracklist.json");
        string chaptersPath = Path.Combine(outputDirectory, $"{safeName}-youtube.txt");
        await File.WriteAllTextAsync(
                tracklistPath,
                JsonSerializer.Serialize(tracks, TracklistJson),
                cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllLinesAsync(
                chaptersPath,
                tracks.Select(t => $"{t.Timestamp} {Credit(t)}"),
                cancellationToken)
            .ConfigureAwait(false);

        // Measure what was actually produced rather than assuming the target was hit.
        double? lufs = await _loudnessMeter
            .MeasureIntegratedLufsAsync(audioPath, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Exported '{Name}' to {Path} ({Seconds:F0}s, {Lufs} LUFS)",
            name, audioPath, project.DurationSeconds, lufs?.ToString("F1") ?? "unmeasured");

        return new SetMixExport(
            Rendered: true, audioPath, tracklistPath, chaptersPath,
            Math.Round(project.DurationSeconds, 1),
            lufs is null ? null : Math.Round(lufs.Value, 2),
            LimiterSettings.Default.CeilingDbTp,
            issues, tracks);
    }

    private static readonly JsonSerializerOptions TracklistJson = new() { WriteIndented = true };

    private static string Credit(MixTrackEntry track)
        => string.IsNullOrWhiteSpace(track.Artist) ? track.Title : $"{track.Artist} - {track.Title}";

    // mm:ss, or h:mm:ss once past the hour — the form YouTube parses into chapters.
    private static string Timestamp(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0.0, seconds));
        return t.TotalHours >= 1.0
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }

    private static IReadOnlyList<MixTrackEntry> Tracklist(
        StudioProject project, IReadOnlyList<MusicTrack> catalog)
    {
        Dictionary<string, MusicTrack> byPath = catalog
            .GroupBy(t => t.File.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return project.Clips
            .OrderBy(c => c.TimelineStartSeconds)
            .Select((clip, index) =>
            {
                byPath.TryGetValue(clip.TrackPath, out MusicTrack? track);
                return new MixTrackEntry(
                    index + 1,
                    track?.Artist,
                    track?.Title ?? Path.GetFileNameWithoutExtension(clip.TrackPath),
                    Math.Round(clip.TimelineStartSeconds, 2),
                    Timestamp(clip.TimelineStartSeconds));
            })
            .ToList();
    }

    /// <summary>
    /// What would embarrass the owner if this mix went public. Each issue names its remedy, because a
    /// refusal the caller cannot act on is just an obstacle.
    /// </summary>
    private static IReadOnlyList<MixGateIssue> PublishGate(StudioProject project)
    {
        var issues = new List<MixGateIssue>();

        foreach (StudioClip clip in project.Clips.OrderBy(c => c.TimelineStartSeconds))
        {
            string where = Path.GetFileNameWithoutExtension(clip.TrackPath);

            // An unwarped clip runs at its own tempo against the set tempo, so it drifts for its whole
            // length — the single worst defect a "beat-matched" mix can ship with.
            if (!clip.WarpEnabled && clip.SourceBpm > 0 && Math.Abs(clip.SourceBpm - project.Bpm) > TempoMatchToleranceBpm)
            {
                issues.Add(new MixGateIssue(
                    where,
                    $"plays at its native {clip.SourceBpm:F2} BPM against the set's {project.Bpm:F2}, so it drifts",
                    "Re-analyze the track so its tempo is trusted, or rebuild the set with " +
                    "excludeLowGridConfidence: true to leave it out."));
            }

            // Unity gain means no loudness measurement, so this clip steps in level against its neighbours.
            if (Math.Abs(clip.Gain - 1.0) < GainEpsilon)
            {
                issues.Add(new MixGateIssue(
                    where,
                    "sits at unity gain, so it is not level-matched to the rest of the mix",
                    "Run measure_catalog_loudness, then rebuild the set."));
            }
        }

        IReadOnlyList<Join> joins = Joins(project);
        double minBlendSeconds = SetBuildOptions.MinOverlapBars * SetBuildOptions.BarSeconds(project.Bpm);
        foreach (Join join in joins)
        {
            double blend = join.EndSeconds - join.StartSeconds;
            if (blend + BlendToleranceSeconds < minBlendSeconds)
            {
                issues.Add(new MixGateIssue(
                    $"{Path.GetFileNameWithoutExtension(join.FromPath)} → {Path.GetFileNameWithoutExtension(join.ToPath)}",
                    $"blends for only {blend:F1}s, under the {minBlendSeconds:F1}s floor, so it reads as a cut",
                    "Rebuild with a longer overlapBars, or fix the grid confidence that forced the clamp."));
            }
        }

        return issues;
    }

    /// <summary>A clip within this of the set tempo needs no stretch, so leaving it unwarped is correct.</summary>
    private const double TempoMatchToleranceBpm = 0.01;

    /// <summary>Gain this close to 1.0 is unity — i.e. no loudness measurement was applied.</summary>
    private const double GainEpsilon = 1e-6;

    /// <summary>Rounding slack when comparing a blend against the bar-derived floor.</summary>
    private const double BlendToleranceSeconds = 0.05;

    /// <summary>Where two consecutive clips overlap: the incoming clip's start until the outgoing one ends.</summary>
    private readonly record struct Join(string FromPath, string ToPath, double StartSeconds, double EndSeconds);

    // Derived from the arrangement rather than stored, so it works for any saved project — including one
    // the user has since edited in STUDIO.
    private static IReadOnlyList<Join> Joins(StudioProject project)
    {
        var joins = new List<Join>();
        for (int i = 0; i + 1 < project.Clips.Count; i++)
        {
            StudioClip from = project.Clips[i];
            StudioClip to = project.Clips[i + 1];
            double factor = WarpMath.WarpFactorAt(from, project.EffectiveTempo, project.Bpm, from.TimelineStartSeconds);
            double fromEnd = from.SourceDuration is { } duration
                ? from.TimelineStartSeconds + WarpMath.WarpedTimelineSeconds(duration.TotalSeconds, factor)
                : to.TimelineStartSeconds;

            joins.Add(new Join(from.TrackPath, to.TrackPath, to.TimelineStartSeconds, Math.Max(to.TimelineStartSeconds, fromEnd)));
        }

        return joins;
    }

    private static DjSetResult Describe(DjSetPlan plan, Dictionary<string, MusicTrack> byPath)
    {
        double[] nativeBpm = plan.Project.Clips.Select(c => c.SourceBpm).Where(b => b > 0).ToArray();
        var warningCounts = plan.Transitions
            .SelectMany(t => t.Warnings)
            .GroupBy(w => w.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        return new DjSetResult(
            ProjectName: plan.Project.Name,
            SavedAs: plan.Project.Name,
            TrackCount: plan.TrackCount,
            TotalSeconds: Math.Round(plan.TotalSeconds, 1),
            TempoBpm: plan.TempoBpm,
            MaxWarpPercent: Math.Round(plan.MaxWarpPercent, 2),
            NativeBpmMin: nativeBpm.Length == 0 ? 0 : nativeBpm.Min(),
            NativeBpmMax: nativeBpm.Length == 0 ? 0 : nativeBpm.Max(),
            PhaseLockedCount: plan.PhaseLockedCount,
            Tracks: plan.Project.Clips.Select((clip, i) => Track(i, clip, byPath, plan.TempoBpm)).ToArray(),
            Transitions: plan.Transitions.Select(t => Transition(t, byPath)).ToArray(),
            RejectedCount: plan.Rejected.Count,
            RejectedCandidates: plan.Rejected
                .Take(MaxReportedRejections)
                .Select(r => new RejectedTrackInfo(r.Path, r.Title, r.Reason.ToString(), r.NeededWarpPercent))
                .ToArray(),
            WarningSummary: warningCounts);
    }

    private static SetTrackInfo Track(int position, StudioClip clip, Dictionary<string, MusicTrack> byPath, double tempoBpm)
    {
        byPath.TryGetValue(clip.TrackPath, out MusicTrack? track);
        double warp = clip.WarpEnabled && clip.SourceBpm > 0 ? ((tempoBpm / clip.SourceBpm) - 1.0) * 100.0 : 0.0;

        return new SetTrackInfo(
            position,
            clip.TrackPath,
            track?.Title ?? Path.GetFileNameWithoutExtension(clip.TrackPath),
            track?.Artist,
            clip.DeckSlot,
            Math.Round(clip.TimelineStartSeconds, 3),
            clip.SourceBpm,
            Math.Round(warp, 2),
            clip.WarpEnabled);
    }

    private static TransitionInfo Transition(SetTransition transition, Dictionary<string, MusicTrack> byPath)
        => new(
            transition.Index,
            transition.FromPath,
            transition.ToPath,
            Title(transition.FromPath, byPath),
            Title(transition.ToPath, byPath),
            transition.StartSeconds,
            transition.EndSeconds,
            transition.OverlapBars,
            transition.OverlapSeconds,
            transition.Type.ToString(),
            Anchor(transition.OutAnchor),
            Anchor(transition.InAnchor),
            transition.TempoBpm,
            transition.FromWarpPercent,
            transition.ToWarpPercent,
            transition.KeyFrom,
            transition.KeyTo,
            transition.KeyRelationship,
            transition.PhaseLocked,
            transition.GridConfidenceFrom is { } from ? Math.Round(from, 3) : null,
            transition.GridConfidenceTo is { } to ? Math.Round(to, 3) : null,
            transition.Warnings.Select(w => w.ToString()).ToArray());

    private static MixAnchorInfo Anchor(MixAnchor anchor)
        => new(Math.Round(anchor.SourceSeconds, 3), anchor.SectionLabel, anchor.Source.ToString());

    private static string Title(string path, Dictionary<string, MusicTrack> byPath)
        => byPath.TryGetValue(path, out MusicTrack? track) ? track.Title : Path.GetFileNameWithoutExtension(path);

    private static Dictionary<string, MusicTrack> ByPath(IReadOnlyList<MusicTrack> pool)
    {
        var byPath = new Dictionary<string, MusicTrack>(StringComparer.OrdinalIgnoreCase);
        foreach (MusicTrack track in pool)
            byPath[track.File.Path] = track;
        return byPath;
    }

    // Numbered so the previews sort in play order, with the track names kept short and filesystem-safe.
    private static string PreviewFileName(string setName, int index, Join join)
    {
        string from = Path.GetFileNameWithoutExtension(join.FromPath);
        string to = Path.GetFileNameWithoutExtension(join.ToPath);
        return Sanitize($"{setName}-{index + 1:D2}-{Shorten(from)}-to-{Shorten(to)}.wav");
    }

    private static string Shorten(string name) => name.Length <= 28 ? name : name[..28];

    private static string Sanitize(string fileName)
        => string.Concat(fileName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
