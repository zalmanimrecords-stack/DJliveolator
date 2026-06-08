using Liveolator.Core.Actions;

namespace Liveolator.Core.Mapping.Profiles;

/// <summary>
/// A default <see cref="ControllerMappingProfile"/> for the Behringer CMD STUDIO 2A (doc 07): a
/// dual-deck DJ controller with two jog wheels, two channel faders, a crossfader, 3-band EQ + a
/// filter knob per channel, and transport/sync buttons. It maps those controls to the existing
/// deck/mixer/beat <see cref="PerformanceActionKind"/>s so the hardware drives the one dispatcher.
/// </summary>
/// <remarks>
/// <para>
/// The note/CC numbers below are a sensible <b>default layout</b>, NOT a hardcoded device driver. The
/// CMD STUDIO 2A's exact MIDI map must be confirmed from its implementation chart or captured per
/// control via <see cref="MidiLearnSession"/> (doc 05/07) — every binding here is a plain
/// <see cref="ControllerBinding"/> the performer can override in learn mode, and the
/// <see cref="MappingConflictDetector"/> guards against accidental collisions.
/// </para>
/// <para>
/// Convention: <b>Deck A = MIDI channel 0 / action slot 0</b>, <b>Deck B = MIDI channel 1 / action
/// slot 1</b>. The deck handler addresses decks by <see cref="PerformanceAction.Slot"/> and the mixer
/// EQ handler reads the band name from <see cref="PerformanceAction.Argument"/> (Low/Mid/High).
/// </para>
/// </remarks>
public static class CmdStudio2AProfile
{
    /// <summary>The profile name persisted/shown in the Mappings UI.</summary>
    public const string ProfileName = "CMD STUDIO 2A (default)";

    /// <summary>Substring matched against the device name to auto-select this profile (doc 05).</summary>
    public const string DeviceHint = "CMD Studio 2A";

    private const int DeckAChannel = 0;
    private const int DeckBChannel = 1;
    private const int DeckASlot = 0;
    private const int DeckBSlot = 1;

    // --- Default note numbers (transport/sync buttons) — learn-overridable. ---
    private const int PlayPauseNote = 0x3B;   // 59
    private const int SyncNote = 0x40;         // 64
    private const int CueNote = 0x42;          // 66

    // --- Default CC numbers (continuous controls) — learn-overridable. ---
    private const int CrossfaderCc = 0x01;     // shared (deck-agnostic) crossfader
    private const int ChannelFaderCc = 0x07;   // per-channel volume fader
    private const int EqHighCc = 0x10;
    private const int EqMidCc = 0x11;
    private const int EqLowCc = 0x12;
    private const int FilterCc = 0x13;
    private const int JogCc = 0x21;            // jog wheel (relative / endless)
    private const double JogTicksPerRevolution = 128.0;

    /// <summary>The default CMD STUDIO 2A mapping profile.</summary>
    public static ControllerMappingProfile Default { get; } = Build();

    private static ControllerMappingProfile Build()
    {
        var bindings = new List<ControllerBinding>();

        // Crossfader is deck-agnostic: one absolute fader on Deck A's channel, slot 0 (the handler
        // applies it across both decks).
        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, DeckAChannel, CrossfaderCc,
            PerformanceActionKind.MixerCrossfade, ActionInputMode.Absolute, DeckASlot));

        AddDeck(bindings, DeckAChannel, DeckASlot);
        AddDeck(bindings, DeckBChannel, DeckBSlot);

        return new ControllerMappingProfile(ProfileName, DeviceHint, bindings);
    }

    // Adds the per-deck strip: play/pause + sync buttons, channel fader, 3-band EQ, filter, and the
    // jog-wheel nudge. Each deck lives on its own MIDI channel so the same CC numbers can repeat
    // across decks without colliding (the CMD STUDIO 2A's mirrored deck layout).
    private static void AddDeck(List<ControllerBinding> bindings, int channel, int slot)
    {
        bindings.Add(new ControllerBinding(
            MidiMessageType.NoteOn, channel, PlayPauseNote,
            PerformanceActionKind.DeckPlayPause, ActionInputMode.Momentary, slot));

        bindings.Add(new ControllerBinding(
            MidiMessageType.NoteOn, channel, SyncNote,
            PerformanceActionKind.DeckSyncToggle, ActionInputMode.Toggle, slot));

        bindings.Add(new ControllerBinding(
            MidiMessageType.NoteOn, channel, CueNote,
            PerformanceActionKind.DeckCue, ActionInputMode.Momentary, slot));

        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, channel, ChannelFaderCc,
            PerformanceActionKind.MixerChannelGain, ActionInputMode.Absolute, slot));

        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, channel, EqHighCc,
            PerformanceActionKind.MixerEqBand, ActionInputMode.Absolute, slot, Argument: "High"));
        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, channel, EqMidCc,
            PerformanceActionKind.MixerEqBand, ActionInputMode.Absolute, slot, Argument: "Mid"));
        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, channel, EqLowCc,
            PerformanceActionKind.MixerEqBand, ActionInputMode.Absolute, slot, Argument: "Low"));

        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, channel, FilterCc,
            PerformanceActionKind.MixerFilter, ActionInputMode.Absolute, slot));

        // The endless jog reports relative ticks. Conversion normalizes them to a fraction of a
        // wheel revolution; DeckActionHandler then applies DJ-appropriate playing/paused sensitivity.
        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, channel, JogCc,
            PerformanceActionKind.DeckJog, ActionInputMode.Relative, slot,
            RelativeTicksPerRevolution: JogTicksPerRevolution));
    }

    /// <summary>
    /// Upgrades the shipped profile's former jog-to-beat-clock mapping without disturbing learned
    /// controls. The exact legacy channel/CC/slot tuple identifies only the old default binding.
    /// </summary>
    public static ControllerMappingProfile UpgradeLegacyJogBindings(ControllerMappingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        bool changed = false;
        IReadOnlyList<ControllerBinding> bindings = profile.Bindings.Select(binding =>
        {
            bool isLegacyJog =
                binding.TriggerType == MidiMessageType.ControlChange
                && binding.Data1 == JogCc
                && binding.InputMode == ActionInputMode.Relative
                && binding.Action == PerformanceActionKind.BeatNudgeForward
                && ((binding.Channel == DeckAChannel && binding.Slot == DeckASlot)
                    || (binding.Channel == DeckBChannel && binding.Slot == DeckBSlot));

            if (!isLegacyJog)
                return binding;

            changed = true;
            return binding with
            {
                Action = PerformanceActionKind.DeckJog,
                RelativeTicksPerRevolution = JogTicksPerRevolution,
            };
        }).ToList();

        return changed ? profile with { Bindings = bindings } : profile;
    }

    public static ControllerMappingProfile UpgradeLegacySyncBindings(ControllerMappingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        bool changed = false;
        IReadOnlyList<ControllerBinding> bindings = profile.Bindings.Select(binding =>
        {
            if (binding.Action != PerformanceActionKind.DeckSyncOnce || binding.Slot is < 0 or > 1)
                return binding;

            changed = true;
            return binding with
            {
                Action = PerformanceActionKind.DeckSyncToggle,
                InputMode = ActionInputMode.Toggle,
            };
        }).ToList();

        return changed ? profile with { Bindings = bindings } : profile;
    }
}
