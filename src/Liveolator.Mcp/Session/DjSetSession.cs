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
    /// that nothing reports. A mix that comes back silent — a source that decoded to nothing, a non-finite
    /// measured loudness, or mostly-silent output — fails on the same terms, after the render; see
    /// <see cref="RequireAudibleMix"/>.</para>
    /// </summary>
    /// <exception cref="ArgumentException">No set is saved under that name, or a source file is missing.</exception>
    /// <exception cref="InvalidOperationException">The render produced a mix that is silent where it
    /// should play. Force does not bypass this.</exception>
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
        // The catalog is passed rather than the planner's warnings being remembered: re-deriving each join's
        // quality here works on sets saved long before the audit existed, and stays correct after STUDIO moves
        // a clip — which a stored report could not, since it would then vouch for an arrangement that is gone.
        var issues = PublishGate(project, catalog).ToList();

        if (issues.Any(i => i.Blocking) && !force)
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
        MixRenderResult render = await _renderer
            .RenderAsync(project, audioPath, sampleRate, progress: null, cancellationToken)
            .ConfigureAwait(false);

        // Measure what was actually produced rather than assuming the target was hit.
        double? lufs = await _loudnessMeter
            .MeasureIntegratedLufsAsync(audioPath, cancellationToken).ConfigureAwait(false);

        // Before any artifact is written: a mix that does not contain the audio it claims to is not a
        // deliverable, and half a publish package is worse than none.
        RequireAudibleMix(name, audioPath, render, lufs);

        // Two defects only the finished render can show: a clip that came out in mono, and a hole in the
        // written audio. Neither is visible in the arrangement, and whole-file loudness provably misses the
        // second (a 95%-silent mix once measured a healthy -10.30 LUFS).
        issues.AddRange(RenderIssues(render));
        if (issues.Any(i => i.Blocking) && !force)
        {
            _logger.LogInformation("Refused to publish '{Name}': the render itself is defective", name);
            return new SetMixExport(
                Rendered: false, audioPath, TracklistPath: null, ChaptersPath: null,
                DurationSeconds: Math.Round(project.DurationSeconds, 1),
                IntegratedLufs: lufs is null ? null : Math.Round(lufs.Value, 2),
                CeilingDbTp: LimiterSettings.Default.CeilingDbTp,
                Issues: issues, Tracks: tracks);
        }

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

    /// <summary>
    /// Refuses a mix that does not contain the audio it claims to.
    /// <para>Sits deliberately outside the publish gate, so <c>force</c> does not reach past it — the same
    /// standing as an unreachable source file, and for the same reason. Every decode failure degrades to
    /// silence rather than a throw (global #16/#26), so without this a 69-minute unattended render of
    /// digital silence reports success. It did: every warped clip decoded to nothing because BASS was not
    /// initialised in the render host, and whole-file loudness read a healthy -10.3 LUFS because the two
    /// clips that happened to need no warp carried the average.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The mix is silent where it should not be.</exception>
    private void RequireAudibleMix(string name, string audioPath, MixRenderResult render, double? lufs)
    {
        if (render.SilentSources.Count > 0)
        {
            _logger.LogError(
                "Export of '{Name}' produced no audio for {Count} of {Total} source(s): {Sources}",
                name, render.SilentSources.Count, render.SourceCount, string.Join(", ", render.SilentSources));
            throw new InvalidOperationException(
                $"{render.SilentSources.Count} of {render.SourceCount} source(s) decoded to nothing, so the mix " +
                $"is silent where they should play: {string.Join(", ", render.SilentSources.Take(3))}" +
                (render.SilentSources.Count > 3 ? $" (+{render.SilentSources.Count - 3} more)" : string.Empty) +
                $". Nothing was published. The incomplete file is at {audioPath} — check the app log for the " +
                "decode warnings, and that the native BASS libraries (including bassflac for flac sources) are " +
                "present next to the render host.");
        }

        // Null is "not measured" and stays a normal outcome; a measured -infinity is a silent file.
        if (lufs is { } measured && !double.IsFinite(measured))
        {
            _logger.LogError("Export of '{Name}' measured {Lufs} LUFS — the file carries no signal", name, measured);
            throw new InvalidOperationException(
                $"The rendered mix measured {measured} LUFS, which means it carries no signal. Nothing was " +
                $"published. The incomplete file is at {audioPath}.");
        }

        if (render.SilentFraction > MaxSilentFraction)
        {
            _logger.LogError(
                "Export of '{Name}' is {Percent:P0} silence", name, render.SilentFraction);
            throw new InvalidOperationException(
                $"{render.SilentFraction:P0} of the rendered mix is silence, so it is not a continuous mix. " +
                $"Nothing was published. The incomplete file is at {audioPath}.");
        }
    }

    /// <summary>
    /// How much of a mix may be silence before it is refused. A continuous mix is silent essentially
    /// nowhere — even a long fade-in and fade-out is under a percent — so half is a floor no plausible
    /// set trips and every broken render does. Kept here rather than in the renderer: measuring the
    /// silence is an audio fact, deciding what is publishable is this gate's business.
    /// </summary>
    private const double MaxSilentFraction = 0.5;

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
    /// <para>Judges the arrangement against <paramref name="catalog"/> rather than against a record of what
    /// the planner intended, so it also answers the questions the geometry alone cannot: whether a blend
    /// opens over beatless material, whether a drop collides with the outgoing record, and whether a clip's
    /// gain hit its limit still short of the set level.</para>
    /// </summary>
    private static IReadOnlyList<MixGateIssue> PublishGate(
        StudioProject project, IReadOnlyList<MusicTrack> catalog)
    {
        var issues = new List<MixGateIssue>();
        Dictionary<string, MusicTrack> byPath = ByPath(catalog);
        double targetLufs = new SetBuildOptions().TargetLufs;

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

            if (!byPath.TryGetValue(clip.TrackPath, out MusicTrack? track))
            {
                // Not knowing is not proof of a defect, so this does not block — but staying silent about a
                // clip nothing is known about is the lie the whole audit exists to stop. Named by full path
                // because the remedy is to go and find that file.
                issues.Add(new MixGateIssue(
                    clip.TrackPath,
                    "is no longer in the catalog, so its loudness, grid and mix points cannot be verified",
                    "Scan its folder again so the set can be judged, or rebuild the set from catalogued tracks.",
                    Blocking: false));
                continue;
            }

            // Unity gain used to mean BOTH "never measured" and "measured exactly at target". ffmpeg's ebur128
            // prints one decimal place, so a track landing on -9.0 is routine — and refusing it told the owner
            // to run a measurement that would change nothing. The catalog settles which case this is.
            if (Math.Abs(clip.Gain - 1.0) < GainEpsilon && track.IntegratedLufs is null)
            {
                issues.Add(new MixGateIssue(
                    where,
                    "sits at unity gain, so it is not level-matched to the rest of the mix",
                    "Run measure_catalog_loudness, then rebuild the set."));
            }

            double residualDb = LoudnessGain.ResidualDb(track.IntegratedLufs, targetLufs);
            if (residualDb > MaxGainResidualDb)
            {
                issues.Add(new MixGateIssue(
                    where,
                    $"measured {track.IntegratedLufs:F1} LUFS and still plays {residualDb:F1} dB under the " +
                    $"set's {targetLufs:F1} target after the boost limit, so it steps down at its join",
                    "Re-master the file or leave it out — no gain setting can rescue it."));
            }
        }

        issues.AddRange(JoinIssues(project, byPath));
        return issues;
    }

    /// <summary>
    /// How far short of the set target a clamped gain may leave a clip before the gate says so.
    /// <para>Read off the arithmetic, not off the anecdote: the +6.02 dB boost limit only bites below −15.02
    /// LUFS against the −9.0 default target, so 1.5 dB of residual means a file measured at or under −16.5
    /// LUFS — quieter than any mastered dance record, and about the smallest level step a listener reliably
    /// hears across a join. Under this the clamp costs less than the error already baked into gaining a
    /// trimmed clip from a whole-file measurement (the −15.3 LUFS track everyone blamed for an 8 dB drop was
    /// left 0.28 dB short by the clamp).</para>
    /// </summary>
    private const double MaxGainResidualDb = 1.5;

    /// <summary>Most holes worth listing before the list stops being read.</summary>
    private const int MaxReportedHoles = 10;

    // Everything decided per join. Split out because the blend-at-the-floor verdict cannot be made one join
    // at a time: one clamped blend is worth reporting, a set that is MOSTLY clamped is a failed arrangement.
    private static IEnumerable<MixGateIssue> JoinIssues(
        StudioProject project, Dictionary<string, MusicTrack> byPath)
    {
        var issues = new List<MixGateIssue>();
        var atFloor = new List<MixGateIssue>();
        IReadOnlyList<Join> joins = Joins(project);
        double setBarSeconds = SetBuildOptions.BarSeconds(project.Bpm);

        foreach (Join join in joins)
        {
            // Both paths, always: the audit collapses the two sides' findings into one verdict per join, so a
            // line naming one record leaves the reader guessing which of the two to act on.
            string where = $"{join.From.TrackPath} → {join.To.TrackPath}";
            double blend = join.EndSeconds - join.StartSeconds;

            // An unwarped clip's blend was cut in bars of ITS OWN tempo, not the set's, so measuring it at the
            // set's bar length reported a legitimate 8-bar blend on a fast unwarped record as under the floor.
            double barSeconds = join.From.WarpEnabled || join.From.SourceBpm <= 0.0
                ? setBarSeconds
                : SetBuildOptions.BarSeconds(join.From.SourceBpm);
            double blendBars = barSeconds > 0.0 ? blend / barSeconds : 0.0;

            if (blendBars < SetBuildOptions.MinOverlapBars - BlendToleranceBars)
            {
                issues.Add(new MixGateIssue(
                    where,
                    $"blends for only {blend:F1}s, under the {SetBuildOptions.MinOverlapBars}-bar floor, so it reads as a cut",
                    "Rebuild with a longer overlapBars, or fix the grid confidence that forced the clamp."));
            }
            else if (blendBars < SetBuildOptions.MinOverlapBars + BlendToleranceBars)
            {
                atFloor.Add(new MixGateIssue(
                    where,
                    $"blends for exactly {SetBuildOptions.MinOverlapBars} bars, sitting on the floor — the " +
                    "requested overlap was clamped all the way down",
                    "Rebuild with a longer overlapBars, or with excludeLowGridConfidence: true to drop the " +
                    "record whose grid forced the clamp (measured: that took one set from 6-of-12 " +
                    "phase-locked joins to 9-of-9)."));
            }

            byPath.TryGetValue(join.From.TrackPath, out MusicTrack? from);
            byPath.TryGetValue(join.To.TrackPath, out MusicTrack? to);
            SetJoinAuditResult audit = SetJoinAudit.Audit(from, to, new SetJoinGeometry(
                MixOutSourceSeconds: SourceSecondsAt(join.From, join.StartSeconds, join.FromWarpFactor),
                MixInSourceSeconds: join.To.SourceIn.TotalSeconds,
                OverlapBars: setBarSeconds > 0.0 ? Math.Max(1, (int)Math.Round(blend / setBarSeconds)) : 1,
                SetTempoBpm: project.Bpm,
                OutgoingWarped: join.From.WarpEnabled,
                IncomingWarped: join.To.WarpEnabled));

            foreach (SetJoinFinding finding in audit.Findings)
            {
                if (JoinIssue(finding, where, audit) is { } issue)
                    issues.Add(issue);
            }
        }

        // Owner decision (2026-08-28): a single clamped blend is reported on every export but does not stop
        // it; only a set where MOST joins collapsed to the floor is an arrangement that failed.
        bool mostAtFloor = atFloor.Count * 2 > joins.Count;
        issues.AddRange(atFloor.Select(i => i with { Blocking = mostAtFloor }));
        return issues;
    }

    // The catalog answers what the geometry cannot. Only the three findings that make a mix unlistenable are
    // raised: the grid and structure findings fire on almost every join of a real psytrance set (one measured
    // set had 8 of 9), so reporting them beside a refusal would train the owner to skim the list.
    private static MixGateIssue? JoinIssue(SetJoinFinding finding, string where, SetJoinAuditResult audit)
        => finding switch
        {
            SetJoinFinding.KicklessMixIn => new MixGateIssue(
                where,
                $"the blend opens over beatless material — only {audit.MixInKickCoverage:P0} of the incoming " +
                "record's blend bars have a kick in them",
                "Rebuild so this record enters where its drums are already running, re-analyze it if its kick " +
                "onsets are wrong, or replace it."),

            SetJoinFinding.JointKicklessRun => new MixGateIssue(
                where,
                $"{audit.JointKicklessBars} consecutive bars inside the blend have no kick on EITHER deck, so " +
                "the mix drops out there",
                "Move one of the two mix points off its breakdown — the hole is both records withdrawing at " +
                "once, so changing either one closes it."),

            SetJoinFinding.DropInsideOverlap => new MixGateIssue(
                where,
                "a drop of the incoming record lands inside the blend, with the outgoing record still playing " +
                "over it",
                "Shorten the overlap or move the entry past that drop, so the incoming arrangement is not " +
                "fighting the outgoing one."),

            // Reported once per clip instead: that line names the file to rescan, and saying it twice per
            // missing track would bury everything else.
            _ => null,
        };

    // Everything the render itself revealed. A hole is reported rather than refused — the audio already
    // exists, and the owner needs the timestamp, not a veto — while a mono clip blocks, because it is a
    // defect of the file about to be published rather than of the arrangement behind it.
    private static IEnumerable<MixGateIssue> RenderIssues(MixRenderResult render)
    {
        foreach (string path in render.MonoFallbackSources)
        {
            yield return new MixGateIssue(
                Path.GetFileNameWithoutExtension(path),
                "rendered in MONO inside a stereo mix: BASS could not decode it, so the render fell back to " +
                "the managed decoder, which has one channel",
                "Check that the native BASS libraries sit next to the render host (bassflac too, for flac " +
                "sources) and that the file itself opens, then export again. force ships it as it is.");
        }

        foreach (MixHole hole in render.Holes.Take(MaxReportedHoles))
        {
            yield return new MixGateIssue(
                $"mix at {Timestamp(hole.StartSeconds)}",
                $"the rendered mix drops to {hole.DeepestDbfs:F1} dBFS for {hole.DurationSeconds:F1}s — far " +
                "deeper than any mastered mix withdraws",
                "Listen at that timestamp: the join there is leaving on, or entering over, beatless material.",
                Blocking: false);
        }

        if (render.Holes.Count > MaxReportedHoles)
        {
            yield return new MixGateIssue(
                "mix",
                $"{render.Holes.Count} holes in total, of which the first {MaxReportedHoles} are listed",
                "Fix those first and export again — a mix with this many withdrawals is not one arrangement.",
                Blocking: false);
        }
    }

    /// <summary>A clip within this of the set tempo needs no stretch, so leaving it unwarped is correct.</summary>
    private const double TempoMatchToleranceBpm = 0.01;

    /// <summary>Gain this close to 1.0 is unity — i.e. no loudness measurement was applied.</summary>
    private const double GainEpsilon = 1e-6;

    /// <summary>Rounding slack on the measured bar count, so a blend cut at exactly the floor reads as being
    /// at the floor rather than under it. Applied to the COUNT, not to the seconds: as a seconds tolerance it
    /// widened the pass condition instead of tightening it, which is how an 8-bar blend went unreported.</summary>
    private const double BlendToleranceBars = 0.05;

    /// <summary>Where two consecutive clips overlap: the incoming clip's start until the outgoing one ends.
    /// Carries the clips themselves because judging the join needs their trim and warp, not just their paths.</summary>
    private readonly record struct Join(
        StudioClip From, StudioClip To, double StartSeconds, double EndSeconds, double FromWarpFactor)
    {
        internal string FromPath => From.TrackPath;

        internal string ToPath => To.TrackPath;
    }

    // Derived from the arrangement rather than stored, so it works for any saved project — including one
    // the user has since edited in STUDIO. Ordered by position rather than by list index for the same
    // reason: a project STUDIO has reordered would otherwise be judged on joins between records that never
    // play together.
    private static IReadOnlyList<Join> Joins(StudioProject project)
    {
        StudioClip[] ordered = project.Clips.OrderBy(c => c.TimelineStartSeconds).ToArray();
        var joins = new List<Join>();
        for (int i = 0; i + 1 < ordered.Length; i++)
        {
            StudioClip from = ordered[i];
            StudioClip to = ordered[i + 1];
            double factor = WarpMath.WarpFactorAt(from, project.EffectiveTempo, project.Bpm, from.TimelineStartSeconds);
            double fromEnd = from.SourceDuration is { } duration
                ? from.TimelineStartSeconds + WarpMath.WarpedTimelineSeconds(duration.TotalSeconds, factor)
                : to.TimelineStartSeconds;

            joins.Add(new Join(from, to, to.TimelineStartSeconds, Math.Max(to.TimelineStartSeconds, fromEnd), factor));
        }

        return joins;
    }

    // Where a clip's playhead sits in its OWN source seconds at a timeline moment: a warped buffer advances
    // by the warp factor per timeline second, an unwarped one 1:1.
    private static double SourceSecondsAt(StudioClip clip, double timelineSeconds, double warpFactor)
        => clip.SourceIn.TotalSeconds + ((timelineSeconds - clip.TimelineStartSeconds) * warpFactor);

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
                // The two entries that explain why the set is SHORT must survive the truncation. A
                // whole-catalog build now overflows the cap easily on uniform lines ("no key", "no harmonic
                // match"), and losing the length-cap line to twenty of those is how an honoured cap reads as
                // a rejection-free build. OrderBy is stable, so everything else keeps the arranger's order.
                .OrderBy(r => r.Reason is RejectReason.LengthCapReached or RejectReason.NoMixOutRunway ? 0 : 1)
                .Take(MaxReportedRejections)
                .Select(r => new RejectedTrackInfo(r.Path, r.Title, r.Reason.ToString(), r.NeededWarpPercent))
                .ToArray(),
            WarningSummary: warningCounts,
            Advisories: Advisories(plan));
    }

    /// <summary>
    /// What to change about the REQUEST, as opposed to about a candidate.
    /// <para>Only one advisory so far, and it is the one the measurements earned: on the set the owner kept,
    /// <c>excludeLowGridConfidence</c> took the phase-locked joins from 6-of-12 to 9-of-9. The flag stays off
    /// by default (flipping it would silently shrink the pool for every existing caller), so the build has to
    /// say so — and the counterfactual costs nothing to state, because with every untrusted grid removed each
    /// surviving join is phase-locked by construction.</para>
    /// </summary>
    private static IReadOnlyList<string> Advisories(DjSetPlan plan)
    {
        int notLocked = plan.Transitions.Count - plan.PhaseLockedCount;
        if (notLocked * 2 <= plan.Transitions.Count)
            return Array.Empty<string>();

        return new[]
        {
            $"{notLocked} of {plan.Transitions.Count} joins are not phase-locked, so each is clamped to the " +
            $"{SetBuildOptions.MinOverlapBars}-bar floor instead of the overlap you asked for. Rebuilding " +
            "with excludeLowGridConfidence: true leaves out every track whose beat grid is untrusted, which " +
            "makes ALL surviving joins phase-locked by construction — at the cost of a shorter set.",
        };
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
