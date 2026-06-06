namespace Liveolator.Core.Audio;

/// <summary>
/// What pressing a deck's Cue button should do, given the deck's transport state and its temporary
/// (primary) cue point — the standard CDJ "back-to-cue" vocabulary (doc 11/22 A5).
/// </summary>
public enum CueButtonAction
{
    /// <summary>Set the temporary cue at the current playhead and pause there (a fresh cue point).</summary>
    SetCueHere,

    /// <summary>Jump the playhead back to the stored cue point and pause there ("back to cue").</summary>
    ReturnToCue,
}

/// <summary>
/// Pure decision logic for the deck Cue button (doc 11/22 A5): turns the deck's play state, current
/// position, and stored temporary-cue position into a <see cref="CueButtonAction"/>. Kept in Core, with
/// no engine/native dependency, so the CDJ semantics unit-test in isolation; the engine then performs
/// the chosen seek/pause.
/// </summary>
/// <remarks>
/// CDJ semantics modelled:
/// <list type="bullet">
/// <item>Playing → <see cref="CueButtonAction.ReturnToCue"/> (jump back to the cue and pause).</item>
/// <item>Paused away from the cue → <see cref="CueButtonAction.SetCueHere"/> (drop a new temp cue).</item>
/// <item>Paused already at the cue → <see cref="CueButtonAction.ReturnToCue"/> (idempotent; the
/// press-and-hold cue-play preview needs a button release the action seam does not carry yet, so the
/// held preview is deferred — pressing at the cue simply holds there).</item>
/// </list>
/// "At the cue" uses a small fractional tolerance so floating-point position jitter does not flip the
/// decision between set and return.
/// </remarks>
public static class CueButtonResolver
{
    /// <summary>Default "at the cue" tolerance as a 0..1 position fraction (~a few ms on a typical track).</summary>
    public const double DefaultAtCueTolerance = 1e-4;

    /// <summary>
    /// Decide the Cue action. <paramref name="cuePositionFraction"/> is the deck's stored temporary cue
    /// (null when none is set yet), <paramref name="currentPositionFraction"/> the live playhead, both
    /// as 0..1 fractions. <paramref name="isPlaying"/> is the deck's transport state.
    /// </summary>
    public static CueButtonAction Resolve(
        bool isPlaying,
        double currentPositionFraction,
        double? cuePositionFraction,
        double atCueTolerance = DefaultAtCueTolerance)
    {
        // No cue set yet: a press while paused drops the cue here; while playing it falls back to
        // returning (to the implicit start) — matching the engine's "return, else start" contract.
        if (cuePositionFraction is not { } cue)
            return isPlaying ? CueButtonAction.ReturnToCue : CueButtonAction.SetCueHere;

        if (isPlaying)
            return CueButtonAction.ReturnToCue;

        bool atCue = Math.Abs(currentPositionFraction - cue) <= Math.Max(0.0, atCueTolerance);
        return atCue ? CueButtonAction.ReturnToCue : CueButtonAction.SetCueHere;
    }
}
