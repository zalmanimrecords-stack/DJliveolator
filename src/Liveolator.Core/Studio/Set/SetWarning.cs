namespace Liveolator.Core.Studio.Set;

/// <summary>
/// The closed vocabulary of things that were compromised while planning one transition. Closed on
/// purpose: the consumer is an AI agent iterating on the set it just asked for, and free text is
/// something it can only guess at — an enum it can branch on.
/// </summary>
public enum SetWarning
{
    /// <summary>The track's grid failed <see cref="Liveolator.Core.Analysis.Bpm.GridConfidence.PhaseSyncReady"/>,
    /// so it plays unwarped and is not phase-locked.</summary>
    LowGridConfidence,

    /// <summary>The track predates grid-confidence analysis, so its grid quality is unknown (re-analyze to know).</summary>
    GridNotAnalyzed,

    /// <summary>No structure analysis at all — the mix points came from the fallback rule.</summary>
    NoStructure,

    /// <summary>Structure exists but failed the trust checks (too few sections, off the beat grid, or
    /// no musical labels), so the fallback rule was used instead.</summary>
    StructureRejected,

    /// <summary>The requested overlap did not fit the available runway and was shortened.</summary>
    OverlapClamped,

    /// <summary>The incoming track's first drop lands inside the crossfade — the outgoing track is still
    /// playing over it.</summary>
    IncomingDropInsideOverlap,

    /// <summary>No kick was found near the incoming mix-in point, so the blend starts over beatless material.</summary>
    NoKickAtMixIn,
}
