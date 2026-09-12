using Liveolator.Core.Actions;

namespace Liveolator.Core.Mapping;

/// <summary>
/// How one <see cref="PerformanceActionKind"/> presents itself as a bindable control: what to call it
/// and how a physical control drives it. Pure vocabulary — no view model, no UI — so the MIDI-learn
/// target list can be generated from the action enum instead of hand-maintained (doc 05/31).
/// </summary>
/// <param name="Label">
/// Human name of the control. For a <paramref name="PerDeck"/> kind this is the bare control name
/// ("Play / Pause") and the caller prefixes the deck ("Deck A: Play / Pause"); for a global kind it is
/// the complete label including its category ("Beat: Tap tempo").
/// </param>
/// <param name="InputMode">The mode a physical control must be learned in to drive this kind correctly.</param>
/// <param name="PerDeck">True when the kind addresses a deck/channel through <c>Slot</c>, so it needs
/// one target per deck rather than a single Slot-0 entry.</param>
/// <param name="Arguments">
/// The fixed set of values the kind requires in <c>PerformanceAction.Argument</c> (EQ bands, stem names,
/// hot-cue pads). One target is offered per value; empty when the kind takes no argument.
/// </param>
public sealed record ActionTarget(
    string Label,
    ActionInputMode InputMode,
    bool PerDeck = false,
    IReadOnlyList<string>? Arguments = null);
