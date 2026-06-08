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
    private PerformanceActionKind _action;
    private int _slot;
    private string? _argument;
    private ActionInputMode? _preferredInputMode;

    /// <inheritdoc />
    public bool IsArmed { get; private set; }

    /// <inheritdoc />
    public event EventHandler<ControllerBinding>? Learned;

    /// <inheritdoc />
    public void Begin(
        PerformanceActionKind action,
        int slot = 0,
        string? argument = null,
        ActionInputMode? preferredInputMode = null)
    {
        _action = action;
        _slot = slot;
        _argument = argument;
        _preferredInputMode = preferredInputMode;
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

        return new ControllerBinding(
            triggerType, message.Channel, message.Data1, _action, inputMode, _slot, _argument);
    }
}
