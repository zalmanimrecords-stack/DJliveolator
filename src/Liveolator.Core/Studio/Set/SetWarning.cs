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

    /// <summary>No kick was found at or after the incoming mix-in point, so the blend opens over beatless
    /// material and stays there — the floor-emptying case. Narrowed from its original meaning, which also
    /// covered the benign <see cref="MixInMovedToKick"/>: one member for both fired on 8 of 9 joins of a set
    /// the owner called excellent, which is how a warning gets trained to be ignored.</summary>
    NoKickAtMixIn,

    // Appended below, never inserted: these names are the wire format MCP callers read.

    /// <summary>The mix-in was moved forward onto the first kick, so the blend opens on the drums instead of
    /// the intro pad. Information about a correction that worked, not a compromise.</summary>
    MixInMovedToKick,

    /// <summary>The track carries no analyzed kick onsets, so nothing could be verified about what is driving
    /// the floor at this join (re-analyze to know). The planner treats it exactly as it did before the
    /// coverage gate existed — silence here used to make an unmeasured record look like a perfect one.</summary>
    KickOnsetsNotAnalyzed,

    /// <summary>Fewer than <see cref="KickCoverage.MixInFloor"/> of the bars the blend opens over carry a kick
    /// on the incoming side. Reported rather than refused: entering on a rising intro is normal practice.</summary>
    LowKickCoverageAtMixIn,
}
