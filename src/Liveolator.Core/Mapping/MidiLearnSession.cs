using Liveolator.Core.Actions;

namespace Liveolator.Core.Mapping;

/// <summary>
/// Single-message learn: the first message after arming becomes the binding. Inference is
/// deliberately simple and predictable — notes → momentary, pitch bend → absolute, CC → absolute —
/// because the design requires the user to be able to override the inferred mode anyway (doc 05);
/// a fragile absolute-vs-relative auto-detector would only get in the way.
/// </summary>
public sealed class MidiLearnSession : IMidiLearnSession
{
    /// <summary>
    /// Fallback encoder resolution applied when a control is learned as relative but the caller
    /// supplied no ticks-per-revolution. A 7-bit encoder reports deltas in the 1..63 range, so a
    /// full sweep is ~128 ticks; binding the raw 1.0 default instead would scrub a whole revolution
    /// per tick (~128x too sensitive — doc 27). The user can still override it afterward.
    /// </summary>
    private const double DefaultRelativeTicksPerRevolution = 128.0;

    private PerformanceActionKind _action;
    private int _slot;
    private string? _argument;
    private ActionInputMode? _preferredInputMode;
    private double _relativeTicksPerRevolution = 1.0;
    private bool _invert;
    private RelativeEncoding _relativeEncoding = RelativeEncoding.TwosComplement;

    /// <inheritdoc />
    public bool IsArmed { get; private set; }

    /// <inheritdoc />
    public event EventHandler<ControllerBinding>? Learned;

    /// <inheritdoc />
    public void Begin(
        PerformanceActionKind action,
        int slot = 0,
        string? argument = null,
        ActionInputMode? preferredInputMode = null,
        double relativeTicksPerRevolution = 1.0,
        bool invert = false,
        RelativeEncoding relativeEncoding = RelativeEncoding.TwosComplement)
    {
        _action = action;
        _slot = slot;
        _argument = argument;
        _preferredInputMode = preferredInputMode;
        _relativeTicksPerRevolution = relativeTicksPerRevolution;
        _invert = invert;
        _relativeEncoding = relativeEncoding;
        IsArmed = true;
    }

    /// <inheritdoc />
    public void Cancel() => IsArmed = false;

    /// <inheritdoc />
    public void Observe(MidiMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!IsArmed)
            return;

        IsArmed = false;
        Learned?.Invoke(this, Infer(message));
    }

    private ControllerBinding Infer(MidiMessage message)
    {
        // Note presses and releases both learn as a momentary NoteOn binding (the canonical press).
        bool isNote = message.Type is MidiMessageType.NoteOn or MidiMessageType.NoteOff;
        MidiMessageType triggerType = isNote ? MidiMessageType.NoteOn : message.Type;
        ActionInputMode inputMode =
            _preferredInputMode
            ?? (isNote ? ActionInputMode.Momentary : ActionInputMode.Absolute);

        // Preserve relative tick scaling so a learned encoder moves the target at a sane rate. When
        // relative is learned without explicit ticks, fall back to a real encoder resolution rather
        // than the raw 1.0 default (doc 27). Absolute/momentary learns keep the unused 1.0.
        double ticksPerRevolution =
            inputMode == ActionInputMode.Relative && _relativeTicksPerRevolution <= 1.0
                ? DefaultRelativeTicksPerRevolution
                : _relativeTicksPerRevolution;

        return new ControllerBinding(
            triggerType, message.Channel, message.Data1, _action, inputMode, _slot, _argument,
            Relative: _relativeEncoding,
            RelativeTicksPerRevolution: ticksPerRevolution,
            Invert: _invert);
    }
}
