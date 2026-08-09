namespace Liveolator.Core.Studio.Set;

/// <summary>
/// Why a candidate track never reached the timeline. Reported per track so the agent can act on it —
/// "6 tracks missed the warp cap by under 1%" is a next call; "the set is short" is not.
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
}
