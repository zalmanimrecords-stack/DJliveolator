using Liveolator.Core.Mapping;

namespace Liveolator.App.Features.Mappings;

public sealed class MappingBindingViewModel
{
    public MappingBindingViewModel(ControllerBinding binding)
    {
        Binding = binding;
        Control = $"{binding.TriggerType}  ch {binding.Channel + 1}  #{binding.Data1}";
        Target = $"{binding.Action}  slot {binding.Slot + 1}";
        Mode = binding.InputMode.ToString();
        MidiIdentity = FormatMidiIdentity(binding);
    }

    public ControllerBinding Binding { get; }
    public string Control { get; }
    public string Target { get; }
    public string Mode { get; }

    /// <summary>
    /// Compact raw-MIDI identity for the control, e.g. "CC 21 ch1", "Note 36 ch10", "PB ch1". Lets a
    /// performer tell two similar controls apart and debug a controller. Channel is shown 1-based; pitch
    /// bend has no note/CC number (it is per-channel) so its data byte is omitted.
    /// </summary>
    public string MidiIdentity { get; }

    private static string FormatMidiIdentity(ControllerBinding binding)
    {
        int channel = binding.Channel + 1; // ControllerBinding.Channel is 0..15; humans count from 1.
        return binding.TriggerType switch
        {
            MidiMessageType.ControlChange => $"CC {binding.Data1} ch{channel}",
            MidiMessageType.NoteOn or MidiMessageType.NoteOff => $"Note {binding.Data1} ch{channel}",
            MidiMessageType.PitchBend => $"PB ch{channel}",
            _ => $"ch{channel}",
        };
    }
}
