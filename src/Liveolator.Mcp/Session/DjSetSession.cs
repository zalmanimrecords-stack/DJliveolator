using Liveolator.Audio.Render;
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
    private readonly ILogger<DjSetSession> _logger;

    public DjSetSession(
        LibrarySession library,
        IStudioProjectStore store,
        OfflineMixRenderer renderer,
        ILogger<DjSetSession> logger)
    {
        _library = library;
        _store = store;
        _renderer = renderer;
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
