using Liveolator.Core.Actions;

namespace Liveolator.Core.Mapping;

/// <summary>
/// Decides whether an inbound message triggers a binding. Isolated so the matching rules —
/// including the NoteOn-velocity-0 convention and pitch bend being per-channel — are tested on
/// their own (doc 05).
/// </summary>
public static class BindingMatcher
{
    /// <summary>
    /// A NoteOn with velocity 0 is, by MIDI convention, a NoteOff. Normalizing here lets bindings
    /// reason about press/release without every caller re-checking velocity.
    /// </summary>
    public static MidiMessageType EffectiveType(MidiMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Type == MidiMessageType.NoteOn && message.Data2 == 0
            ? MidiMessageType.NoteOff
            : message.Type;
    }

    /// <summary>True when <paramref name="binding"/> should fire for <paramref name="message"/>.</summary>
    public static bool Matches(ControllerBinding binding, MidiMessage message)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(message);

        MidiMessageType effective = EffectiveType(message);
        if (binding.TriggerType != effective)
        {
            // A release-reporting momentary NoteOn binding also matches its NoteOff (the release), so a
            // press-and-hold gesture sees both edges (doc 31). Every other type mismatch is a non-match.
            if (!(binding.ReportRelease
                  && binding.TriggerType == MidiMessageType.NoteOn
                  && effective == MidiMessageType.NoteOff))
                return false;
        }
        if (binding.Channel != message.Channel)
            return false;
        // Pitch bend carries no note/CC address; it is identified by type + channel alone.
        if (binding.TriggerType == MidiMessageType.PitchBend)
            return true;
        if (binding.Data1 != message.Data1)
            return false;

        // Controllers that expose buttons as CC commonly send 127 on press and 0 on release.
        // Momentary/toggle bindings fire on the press only — otherwise one physical click toggles
        // twice — unless the binding opts into release reporting (then the 0 also fires, as a release).
        if (binding.TriggerType == MidiMessageType.ControlChange
            && binding.InputMode is ActionInputMode.Momentary or ActionInputMode.Toggle)
            return message.Data2 > 0 || binding.ReportRelease;

        return true;
    }

    /// <summary>
    /// True when <paramref name="message"/> is the RELEASE edge of a momentary/toggle
    /// <paramref name="binding"/> (a NoteOff, or a CC button at 0). Only meaningful for a matched
    /// momentary/toggle binding; absolute/relative controls never report a release.
    /// </summary>
    public static bool IsRelease(ControllerBinding binding, MidiMessage message)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(message);

        if (binding.InputMode is not (ActionInputMode.Momentary or ActionInputMode.Toggle))
            return false;
        if (EffectiveType(message) == MidiMessageType.NoteOff)
            return true;
        return message.Type == MidiMessageType.ControlChange && message.Data2 == 0;
    }
}
