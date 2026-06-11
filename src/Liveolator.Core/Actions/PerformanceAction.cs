namespace Liveolator.Core.Actions;

/// <summary>
/// A single, fully self-describing performance intent. A plain record so mappings and show
/// profiles serialize cleanly to JSON (doc 13): the enum plus primitive fields carry
/// everything an engine needs, with no behaviour attached.
/// </summary>
/// <param name="Kind">What to do.</param>
/// <param name="InputMode">How the bound control expresses the request.</param>
/// <param name="Value">Absolute 0..1 (for <see cref="ActionInputMode.Absolute"/>) or a signed
/// delta (for <see cref="ActionInputMode.Relative"/>); ignored for momentary/toggle.</param>
/// <param name="Slot">Target index where the kind addresses one of many — scene, bank, deck,
/// layer, hot-cue, or track position.</param>
/// <param name="Argument">Free-form discriminator a kind may need (e.g. a macro name); null
/// when unused.</param>
/// <param name="Target">Optional stable instance identifier for actions that address a loaded
/// extension instance, such as one VST effect in a rack.</param>
/// <param name="Origin">Optional emitting-source tag (e.g. "automix"). Null = a human gesture
/// (UI/controller). Automation stamps its origin so a human touching the same parameter can be
/// detected and yielded to — automation must never fight the performer (doc 10/11).</param>
public sealed record PerformanceAction(
    PerformanceActionKind Kind,
    ActionInputMode InputMode = ActionInputMode.Momentary,
    double Value = 0,
    int Slot = 0,
    string? Argument = null,
    string? Target = null,
    string? Origin = null);
