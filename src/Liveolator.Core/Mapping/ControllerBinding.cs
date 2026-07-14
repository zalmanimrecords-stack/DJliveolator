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
/// <param name="RelativeTicksPerRevolution">
/// Number of relative encoder ticks in one physical revolution. A value of 1 preserves the raw
/// relative step used by non-jog encoders.
/// </param>
/// <param name="Invert">Reverses the relative control direction.</param>
/// <param name="SoftTakeover">
/// When true, an <see cref="ActionInputMode.Absolute"/> control does not move the target until the
/// incoming hardware value crosses (picks up) the current value, preventing a jump when the physical
/// position differs from the target (doc 27). Defaults to off for backward compatibility; ignored for
/// non-absolute modes.
/// </param>
/// <param name="ReportRelease">
/// When true, a momentary control also fires on release (carrying <c>IsPressed = false</c>), enabling
/// press-and-hold gestures such as cue-play preview and EQ kill. Off by default, so a normal button
/// still fires once on press only (doc 31); ignored for non-momentary modes.
/// </param>
public sealed record ControllerBinding(
    MidiMessageType TriggerType,
    int Channel,
    int Data1,
    PerformanceActionKind Action,
    ActionInputMode InputMode,
    int Slot = 0,
    string? Argument = null,
    ValueCurve Curve = ValueCurve.Linear,
    RelativeEncoding Relative = RelativeEncoding.TwosComplement,
    double RelativeTicksPerRevolution = 1.0,
    bool Invert = false,
    bool SoftTakeover = false,
    bool ReportRelease = false);
