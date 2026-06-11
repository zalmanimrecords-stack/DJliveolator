namespace Liveolator.Core.Automix;

/// <summary>
/// Why auto-mix refused to start — a typed reason so the UI/log gets a human explanation, never a
/// silent failure (global standard #26). Refusal is a feature: a transition that cannot be executed
/// safely is rejected BEFORE anything is audible.
/// </summary>
public enum AutomixRefusal
{
    /// <summary>No refusal — the transition may start.</summary>
    None,

    /// <summary>Neither deck is playing — there is nothing to transition from.</summary>
    NothingPlaying,

    /// <summary>The incoming deck has no track loaded.</summary>
    IncomingNotLoaded,

    /// <summary>One or both decks have no analyzed BPM — sync would be a guess.</summary>
    TempoUnknown,

    /// <summary>Even the octave-folded tempo-match rate exceeds the engine's pitch range.</summary>
    TempoGapTooLarge,

    /// <summary>The outgoing track ends too soon for even the shortest transition.</summary>
    NotEnoughTimeLeft,

    /// <summary>The incoming track is too short to carry the floor after the blend.</summary>
    IncomingTooShort,
}
