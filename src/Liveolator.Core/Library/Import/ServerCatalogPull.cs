using System;
using System.Collections.Generic;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Analysis.Structure;
using Liveolator.Core.Enrichment;
using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Library.Import;

/// <summary>One track where the server's tempo disagrees with the local one, for the preview list.</summary>
public sealed record ServerCatalogDisagreement(string Path, string Title, double LocalBpm, double ServerBpm);

/// <summary>
/// What a pull would do, computed once and consumed twice: the preview renders the counts, Apply writes
/// <see cref="TracksToUpsert"/>. Recomputing for Apply would mean a second network read and a second
/// chance to disagree with what the DJ approved.
/// </summary>
public sealed record ServerCatalogPullPlan(
    IReadOnlyList<MusicTrack> TracksToUpsert,
    int TracksGainingAnalysis,
    int TracksNowPhaseSyncReady,
    int NotFoundLocally,
    int HandCorrectedUntouched,
    int SkippedInUse,
    IReadOnlyList<ServerCatalogDisagreement> Disagreements)
{
    public static ServerCatalogPullPlan Empty { get; } =
        new(Array.Empty<MusicTrack>(), 0, 0, 0, 0, 0, Array.Empty<ServerCatalogDisagreement>());

    /// <summary>
    /// One line for the status bar / confirmation. Leads with the benefit and ends with the protection:
    /// "your hand-corrected tracks were untouched" is the sentence that earns a second run.
    /// </summary>
    public string Describe()
    {
        var parts = new List<string> { $"Added analysis to {TracksGainingAnalysis} track(s)" };
        if (TracksNowPhaseSyncReady > 0) parts.Add($"{TracksNowPhaseSyncReady} can now phase-sync");
        if (Disagreements.Count > 0) parts.Add($"{Disagreements.Count} disagree with your grids (flagged)");
        if (HandCorrectedUntouched > 0) parts.Add($"{HandCorrectedUntouched} hand-corrected track(s) untouched");
        if (NotFoundLocally > 0) parts.Add($"{NotFoundLocally} not found on this machine");
        if (SkippedInUse > 0) parts.Add($"{SkippedInUse} in use - run again after");
        return string.Join(". ", parts) + ".";
    }
}

/// <summary>
/// Merges analysis produced by the scanning server into this machine's catalog - one way, server to
/// local, filling gaps only.
/// </summary>
/// <remarks>
/// <para>
/// Pure and deterministic: it takes two snapshots and returns rows, so the preview the DJ approves and the
/// rows Apply writes are the same objects. It never touches <see cref="MusicLibrary"/>, which matters
/// because several call sites flush the whole in-memory library to disk - a half-merged library would be
/// persisted by whichever of them fired first.
/// </para>
/// <para>
/// It deliberately does NOT route through <see cref="ImportTrackMapper"/>. That mapper stamps
/// <c>AnalysisIsManual = true</c> on every row it contributes a BPM to, which is right for a human-curated
/// Rekordbox collection and catastrophic here: the server contributes a BPM to nearly every row, so the
/// whole catalog would be locked against future re-analysis. It also takes no
/// <see cref="ImportMergePolicy"/> - a pull is fill-gaps by definition, and accepting the enum would
/// invite someone to pass <see cref="ImportMergePolicy.Overwrite"/> and hand a machine the right to
/// bulldoze a DJ's hand-built beat grids.
/// </para>
/// <para>
/// Preservation is by construction, not by enumeration: every row is produced as <c>existing with { }</c>,
/// so any field not named below survives automatically. A field added to <see cref="MusicTrack"/> later is
/// therefore safe by default rather than silently dropped.
/// </para>
/// </remarks>
public static class ServerCatalogPull
{
    /// <summary>A key this weak is treated as absent, matching the online-enrichment rule.</summary>
    private const double WeakKeyConfidence = 0.2;

    /// <summary>Source name recorded against a disagreeing server tempo, shown in the conflict tooltip.</summary>
    public const string DisagreementSource = "server";

    /// <summary>
    /// Work out what pulling <paramref name="serverTracks"/> into <paramref name="localCatalog"/> would do.
    /// Server paths are translated with <see cref="PortablePath.Rebase"/>; a row whose path does not map,
    /// or maps to a file this catalog has never seen, is counted and skipped - v1 enriches existing rows
    /// and never adds new ones.
    /// </summary>
    /// <param name="pathsInUse">
    /// Tracks currently loaded on a deck. Rewriting a loaded track's grid can change how the mixer behaves
    /// under the DJ's hands mid-mix, so those rows are deferred rather than written.
    /// </param>
    /// <summary>
    /// The catalog as it looks once <paramref name="plan"/> is applied: every row the plan upserts
    /// replaces its counterpart by path, and every other row is carried through untouched. Pure — it
    /// returns a new list rather than mutating the library, so the caller decides when the swap happens
    /// and a half-merged catalog can never be persisted by whichever save fires first.
    /// </summary>
    public static IReadOnlyList<MusicTrack> Apply(
        IReadOnlyCollection<MusicTrack> localCatalog, ServerCatalogPullPlan plan)
    {
        ArgumentNullException.ThrowIfNull(localCatalog);
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.TracksToUpsert.Count == 0)
            return localCatalog.ToList();

        Dictionary<string, MusicTrack> merged = new(StringComparer.OrdinalIgnoreCase);
        foreach (MusicTrack track in localCatalog)
            merged[track.File.Path] = track;
        foreach (MusicTrack upsert in plan.TracksToUpsert)
            merged[upsert.File.Path] = upsert;

        return merged.Values.ToList();
    }

    public static ServerCatalogPullPlan Plan(
        IReadOnlyList<MusicTrack> serverTracks,
        IReadOnlyCollection<MusicTrack> localCatalog,
        string? serverPathPrefix,
        string? localPathPrefix,
        IReadOnlySet<string>? pathsInUse = null)
    {
        ArgumentNullException.ThrowIfNull(serverTracks);
        ArgumentNullException.ThrowIfNull(localCatalog);

        Dictionary<string, MusicTrack> local = new(StringComparer.OrdinalIgnoreCase);
        foreach (MusicTrack track in localCatalog)
            local[track.File.Path] = track;

        bool translatingPaths = !string.IsNullOrWhiteSpace(serverPathPrefix)
                                || !string.IsNullOrWhiteSpace(localPathPrefix);

        var upserts = new List<MusicTrack>();
        var disagreements = new List<ServerCatalogDisagreement>();
        int gained = 0, phaseSyncReady = 0, notFound = 0, handCorrected = 0, inUse = 0;

        foreach (MusicTrack server in serverTracks)
        {
            // No prefixes at all means the two machines mount the library at the same path — the common
            // case when both read one UNC share — so the server's own path IS the local one. Rebase
            // answers null for an absent prefix, which would otherwise count every row as "not found"
            // and make the pull a silent no-op exactly where it is easiest to configure.
            string? localPath = translatingPaths
                ? PortablePath.Rebase(server.File.Path, serverPathPrefix, localPathPrefix)
                : server.File.Path;
            if (localPath is null || !local.TryGetValue(localPath, out MusicTrack? existing))
            {
                notFound++;
                continue;
            }

            // The veto. AnalysisIsManual is not merely data, it is the guard that exempts a row from
            // re-analysis; writing anything here would discard the DJ's correction AND re-arm the automatic
            // pass that overwrites it again, leaving no trace a correction ever existed.
            if (existing.AnalysisIsManual)
            {
                handCorrected++;
                continue;
            }

            if (pathsInUse is not null && pathsInUse.Contains(localPath))
            {
                inUse++;
                continue;
            }

            bool gainedAnalysis = false;
            MusicTrack merged = Merge(existing, server, disagreements, ref gainedAnalysis);
            if (ReferenceEquals(merged, existing))
                continue;

            upserts.Add(merged);
            if (gainedAnalysis)
            {
                gained++;
                // Tempo alone only buys a tempo match; the downbeat is what makes phase sync legal, so it
                // is counted separately - it is the number the DJ actually feels.
                if (existing.Bpm is null && merged.Bpm?.DownbeatSeconds is not null)
                    phaseSyncReady++;
            }
        }

        return new ServerCatalogPullPlan(
            upserts, gained, phaseSyncReady, notFound, handCorrected, inUse, disagreements);
    }

    private static MusicTrack Merge(
        MusicTrack existing,
        MusicTrack server,
        List<ServerCatalogDisagreement> disagreements,
        ref bool tookAnalysis)
    {
        BpmResult? bpm = existing.Bpm;
        BpmProvenance provenance = existing.BpmProvenance;
        MediaAnalysisStatus status = existing.Status;
        double? onlineBpm = existing.OnlineBpm;
        string? onlineSource = existing.OnlineBpmSource;

        if (existing.Bpm is null)
        {
            if (server.Bpm is not null)
            {
                // The whole BpmResult, not just the number: the grid anchor, downbeat and kick onsets are
                // what make a track phase-sync eligible, and they are the reason the server exists.
                bpm = server.Bpm;
                provenance = BpmProvenance.LocalDetected;
                status = MediaAnalysisStatus.Ok;
                tookAnalysis = true;
            }
        }
        else if (server.Bpm is not null && existing.BpmProvenance != BpmProvenance.LocalConfirmed)
        {
            // Local wins, always and silently. A disagreement is not resolved during the pull - the DJ
            // reviews it later in the library through the conflict badge that already ships for the online
            // BPM cross-check, which is why this reuses that machinery instead of inventing a second flag.
            EnrichedBpm verdict = MetadataMergePolicy.MergeBpm(existing.Bpm, server.Bpm.Bpm, existing.Status);
            if (verdict.Provenance == BpmProvenance.Conflicted)
            {
                provenance = BpmProvenance.Conflicted;
                onlineBpm = server.Bpm.Bpm;
                onlineSource = DisagreementSource;
                disagreements.Add(new ServerCatalogDisagreement(
                    existing.File.Path, existing.Title, existing.Bpm.Bpm, server.Bpm.Bpm));
            }
        }

        MusicalKey? key = existing.Key;
        if (server.Key is not null && (key is null || key.Confidence < WeakKeyConfidence))
        {
            key = server.Key;
            tookAnalysis = true;
        }

        // Cue points are a struct, so "was one taken" cannot be answered with a null check.
        TrackCues cues = existing.Cues;
        if (cues == TrackCues.None && server.Cues != TrackCues.None)
        {
            cues = server.Cues;
            tookAnalysis = true;
        }

        SongStructure? structure = existing.Structure;
        if (structure is null && server.Structure is not null)
        {
            structure = server.Structure;
            tookAnalysis = true;
        }

        double? lufs = existing.IntegratedLufs ?? server.IntegratedLufs;
        TimeSpan? duration = existing.Duration ?? server.Duration;

        bool changed = tookAnalysis
            || provenance != existing.BpmProvenance
            || lufs != existing.IntegratedLufs
            || duration != existing.Duration;
        if (!changed)
            return existing;

        return existing with
        {
            Bpm = bpm,
            Key = key,
            Cues = cues,
            Structure = structure,
            Duration = duration,
            IntegratedLufs = lufs,
            Status = status,
            BpmProvenance = provenance,
            OnlineBpm = onlineBpm,
            OnlineBpmSource = onlineSource,
            // Taken from the SERVER row, never stamped as now: this machine did not analyze the file, and
            // claiming it did would make "last scanned" a lie and hide a stale server behind a fresh date.
            // AnalyzerVersion likewise - an older server leaves the row eligible for local re-analysis,
            // which is self-healing rather than a special case.
            AnalyzerVersion = tookAnalysis ? server.AnalyzerVersion : existing.AnalyzerVersion,
            LastAnalyzedUtc = tookAnalysis
                ? server.LastAnalyzedUtc ?? existing.LastAnalyzedUtc
                : existing.LastAnalyzedUtc,
            // AnalysisIsManual is deliberately absent: the pull never sets it, and `existing with { }`
            // carries the local value. Rating, PlayCount, LastPlayed, DateAdded, Kind, Metadata, File and
            // OnlineLookupUtc are absent for the same reason - preserved by construction, not by listing.
        };
    }
}
