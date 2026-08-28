namespace Liveolator.Core.Studio.Set;

/// <summary>
/// Why a candidate track never reached the timeline. Reported per track so the agent can act on it —
/// "6 tracks missed the warp cap by under 1%" is a next call; "the set is short" is not. The last members
/// answer the same question about the set as a whole (the cap was honoured, the chain stopped here), so not
/// every entry names an absent track — see <see cref="RejectedCandidate"/>.
/// </summary>
public enum RejectReason
{
    /// <summary>Analysis failed or has not run.</summary>
    NotAnalyzed,

    /// <summary>No detected key, so it cannot take part in harmonic ordering.</summary>
    NoKey,

    /// <summary>No detected tempo, so it cannot be beat-matched.</summary>
    NoBpm,

    /// <summary>Unknown length — a clip with no end would sound over everything after it.</summary>
    NoDuration,

    /// <summary>The file is missing, or is an un-downloaded cloud placeholder (would render as silence).</summary>
    FileUnreachable,

    /// <summary>Reaching the set tempo would warp it past the configured limit.</summary>
    OutsideTempoRange,

    /// <summary>Too short to hold a mix-in, a minimum crossfade, and a mix-out.</summary>
    TooShort,

    /// <summary>Its beat grid is not trustworthy and the caller asked to exclude such tracks.</summary>
    LowGridConfidence,

    // Everything below is APPENDED, never reordered: the member names are the wire format the MCP
    // contract reports as a free string.

    /// <summary>Mixable, but the harmonic chain never reached a track its key is compatible with.
    /// The next move is a wider pool or a different seed, not a wider warp limit.</summary>
    NoHarmonicMatch,

    /// <summary>Compatible in key, but the requested tempo trend forbade the step to it. Rising is
    /// non-decreasing at every point with no lookahead, so a seed at the pool's top tempo closes the
    /// chain immediately — the remedy is to drop the trend or reseed low.</summary>
    BlockedByTrend,

    /// <summary><b>Not a rejection.</b> The requested set length was reached and the remaining candidates
    /// were never tried. Reported so a cap that was honoured does not read as a rejection-free build of a
    /// 1,300-track catalog.</summary>
    LengthCapReached,

    /// <summary>This record had no mix-out left: its own entry point sits too close to its end for even the
    /// shortest legal blend. Blames the OUTGOING record, which is what the false
    /// <see cref="TooShort"/> was hiding — the condition does not involve the incoming track at all. The
    /// chain ends here (it cannot change for a later candidate) and this record stays on as the closing
    /// clip, so this is the one entry that names a track the set does contain.</summary>
    NoMixOutRunway,

    /// <summary>No legal crossfade could be planned into it from the record before it, with a mix-out that
    /// record could still reach. Distinct from <see cref="TooShort"/>, which is about the file's length
    /// alone and is decided before any join is planned.</summary>
    NoTransitionPlanned,

    /// <summary>The seed itself missed the warp cap against the set tempo it helped choose, so the set does
    /// not start where the caller asked. Reported apart from <see cref="OutsideTempoRange"/> because the
    /// remedy is different: reseed, rather than widen the limit.</summary>
    SeedOutsideTempoRange,
}
