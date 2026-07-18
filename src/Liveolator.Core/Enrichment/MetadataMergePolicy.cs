using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Library;

namespace Liveolator.Core.Enrichment;

/// <summary>Where a track's effective BPM came from, after merging local detection with an online lookup.</summary>
public enum BpmProvenance
{
    /// <summary>No BPM from either source.</summary>
    Unknown,

    /// <summary>Locally detected from the file; no online value to compare.</summary>
    LocalDetected,

    /// <summary>Filled from the online source because local detection had none.</summary>
    OnlineFetched,

    /// <summary>Local and online agree (within tolerance, incl. ½×/2×) — high confidence.</summary>
    CrossChecked,

    /// <summary>Local and online disagree — local value kept, flagged for review.</summary>
    Conflicted,

    /// <summary>The user confirmed the local value (manual BPM or a dismissed conflict) — never re-flagged.</summary>
    LocalConfirmed,
}

/// <summary>The merged BPM result: the chosen value, its confidence, where it came from, and the resulting status.</summary>
public sealed record EnrichedBpm(double? Bpm, double Confidence, BpmProvenance Provenance, MediaAnalysisStatus Status);

/// <summary>
/// Merges a locally-detected BPM with an online value (doc 16), <b>offline-first</b>: the local
/// (file-accurate) value always stays authoritative; the online value only cross-checks it (agreement
/// raises confidence and promotes the status to <see cref="MediaAnalysisStatus.Ok"/>) or fills the gap
/// when there is no local value (marked unverified). Pure and deterministic — no network, no I/O.
/// </summary>
public static class MetadataMergePolicy
{
    /// <summary>Two tempos within this many BPM are treated as the same (after ½×/2× normalisation).</summary>
    public const double AgreementToleranceBpm = 2.0;

    /// <summary>Confidence assigned when local and online agree (a cross-checked, trustworthy value).</summary>
    public const double CrossCheckedConfidence = 0.95;

    /// <summary>Confidence assigned to an online-only value (usable, but not verified against the file).</summary>
    public const double OnlineOnlyConfidence = 0.6;

    /// <summary>
    /// Merge a local BPM (or null) with an online BPM (or null), given the track's current analysis
    /// status, into an <see cref="EnrichedBpm"/> with provenance.
    /// </summary>
    public static EnrichedBpm MergeBpm(BpmResult? local, double? online, MediaAnalysisStatus currentStatus)
    {
        bool hasOnline = online is > 0;

        if (local is null)
        {
            return hasOnline
                // No local value to verify against — usable, but flag it as not file-verified.
                ? new EnrichedBpm(online, OnlineOnlyConfidence, BpmProvenance.OnlineFetched, MediaAnalysisStatus.PartiallyAnalyzed)
                : new EnrichedBpm(null, 0.0, BpmProvenance.Unknown, currentStatus);
        }

        if (!hasOnline)
            return new EnrichedBpm(local.Bpm, local.Confidence, BpmProvenance.LocalDetected, currentStatus);

        if (Agrees(local.Bpm, online!.Value))
        {
            // Keep the local (file-accurate) value; the agreement is what we trust and promote.
            double confidence = Math.Max(local.Confidence, CrossCheckedConfidence);
            return new EnrichedBpm(local.Bpm, confidence, BpmProvenance.CrossChecked, MediaAnalysisStatus.Ok);
        }

        // Disagreement: stay offline-first (keep local) but surface it for review.
        return new EnrichedBpm(local.Bpm, local.Confidence, BpmProvenance.Conflicted, MediaAnalysisStatus.PartiallyAnalyzed);
    }

    // Agreement allows half-/double-time, since tempo detectors commonly land an octave off (e.g. a
    // 140 BPM psytrance track detected as 70), and 3:2, the other common detector/crowd-data error
    // (87 vs 130.5 on shuffle/broken-beat). All these relations count as the same tempo.
    private static bool Agrees(double localBpm, double onlineBpm)
        => Within(localBpm, onlineBpm)
        || Within(localBpm, onlineBpm * 2.0)
        || Within(localBpm, onlineBpm / 2.0)
        || Within(localBpm, onlineBpm * 1.5)
        || Within(localBpm, onlineBpm / 1.5);

    private static bool Within(double a, double b) => Math.Abs(a - b) <= AgreementToleranceBpm;
}
