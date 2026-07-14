namespace Liveolator.Core.Mapping;

/// <summary>
/// A set of two or more bindings that respond to the same physical control, reported to the UI so
/// the performer resolves it — the engine never silently picks a winner (doc 05, global #26).
/// </summary>
/// <param name="TriggerType">The shared trigger type.</param>
/// <param name="Channel">The shared channel.</param>
/// <param name="Data1">The shared note/CC number (-1 for pitch bend, which has no address).</param>
/// <param name="Bindings">The colliding bindings.</param>
public sealed record MappingConflict(
    MidiMessageType TriggerType,
    int Channel,
    int Data1,
    IReadOnlyList<ControllerBinding> Bindings);
