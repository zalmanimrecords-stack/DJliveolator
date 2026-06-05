using Liveolator.Core.Actions;

namespace Liveolator.Core.Mapping;

/// <summary>
/// Binds one physical control (a note or CC on a channel) to one performance action. A plain
/// record so profiles serialize to JSON and can be shared (doc 05/13).
/// </summary>
/// <param name="TriggerType">The message shape that fires this binding.</param>
/// <param name="Channel">MIDI channel the control lives on, 0..15.</param>
/// <param name="Data1">Note or CC number that triggers (ignored for pitch bend, which is
/// per-channel).</param>
/// <param name="Action">The action to dispatch.</param>
/// <param name="InputMode">How the control's value is interpreted into the action value.</param>
/// <param name="Slot">Target index carried into the action.</param>
/// <param name="Argument">Free-form argument carried into the action (e.g. a macro name).</param>
/// <param name="Curve">Scaling applied to absolute values.</param>
/// <param name="Relative">Encoder encoding applied to relative values.</param>
public sealed record ControllerBinding(
    MidiMessageType TriggerType,
    int Channel,
    int Data1,
    PerformanceActionKind Action,
    ActionInputMode InputMode,
    int Slot = 0,
    string? Argument = null,
    ValueCurve Curve = ValueCurve.Linear,
    RelativeEncoding Relative = RelativeEncoding.TwosComplement);
