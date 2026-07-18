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
    private const int JogCc = 0x21;            // jog wheel (relative / endless, offset-binary around 64)
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
            PerformanceActionKind.MixerCrossfade, ActionInputMode.Absolute, DeckASlot,
            SoftTakeover: true));

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

        // SYNC = top-level beat lock: a press toggles tempo + phase sync to the other deck, matching the
        // on-screen SYNC control. Release is ignored by the mapper for toggle buttons.
        bindings.Add(new ControllerBinding(
            MidiMessageType.NoteOn, channel, SyncNote,
            PerformanceActionKind.DeckSyncToggle, ActionInputMode.Toggle, slot));

        bindings.Add(new ControllerBinding(
            MidiMessageType.NoteOn, channel, CueNote,
            PerformanceActionKind.DeckCue, ActionInputMode.Momentary, slot));

        // Absolute mixer controls opt into soft-takeover (pickup) so a fader/knob whose physical
        // position differs from the target after a profile/track change does not jump the value (doc 31).
        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, channel, ChannelFaderCc,
            PerformanceActionKind.MixerChannelGain, ActionInputMode.Absolute, slot, SoftTakeover: true));

        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, channel, EqHighCc,
            PerformanceActionKind.MixerEqBand, ActionInputMode.Absolute, slot, Argument: "High", SoftTakeover: true));
        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, channel, EqMidCc,
            PerformanceActionKind.MixerEqBand, ActionInputMode.Absolute, slot, Argument: "Mid", SoftTakeover: true));
        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, channel, EqLowCc,
            PerformanceActionKind.MixerEqBand, ActionInputMode.Absolute, slot, Argument: "Low", SoftTakeover: true));

        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, channel, FilterCc,
            PerformanceActionKind.MixerFilter, ActionInputMode.Absolute, slot, SoftTakeover: true));

        // The endless jog reports relative ticks as OFFSET-BINARY around 64 (rest = 64, forward > 64,
        // backward < 64) — the Behringer CMD hardware, like Pioneer and every mainstream DJ deck. Decoding
        // it as two's-complement turns 0x40 (rest) into -64 and flips direction, so each tick became a
        // near-half-revolution jump the wrong way. Conversion normalizes the offset to a fraction of a
        // wheel revolution; DeckActionHandler then applies DJ-appropriate playing/paused sensitivity.
        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, channel, JogCc,
            PerformanceActionKind.DeckJog, ActionInputMode.Relative, slot,
            Relative: RelativeEncoding.OffsetBinary,
            RelativeTicksPerRevolution: JogTicksPerRevolution));
    }

    /// <summary>
    /// Heals a saved jog binding on load, without disturbing learned controls: (1) retargets the very
    /// first shipped layout, where the jog drove the beat clock instead of the deck; and (2) rewrites a
    /// jog still decoded as two's-complement to the DJ-standard offset-binary. Idempotent — returns the
    /// same instance when nothing needs healing, so the session only re-saves on a real change.
    /// </summary>
    public static ControllerMappingProfile UpgradeLegacyJogBindings(ControllerMappingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        bool changed = false;
        IReadOnlyList<ControllerBinding> bindings = profile.Bindings.Select(binding =>
        {
            // (1) Retarget the old default where the jog CC drove BeatNudgeForward instead of the deck.
            // The exact channel/CC/slot tuple identifies only that legacy binding.
            bool isLegacyBeatJog =
                binding.TriggerType == MidiMessageType.ControlChange
                && binding.Data1 == JogCc
                && binding.InputMode == ActionInputMode.Relative
                && binding.Action == PerformanceActionKind.BeatNudgeForward
                && ((binding.Channel == DeckAChannel && binding.Slot == DeckASlot)
                    || (binding.Channel == DeckBChannel && binding.Slot == DeckBSlot));

            if (isLegacyBeatJog)
            {
                changed = true;
                binding = binding with
                {
                    Action = PerformanceActionKind.DeckJog,
                    RelativeTicksPerRevolution = JogTicksPerRevolution,
                };
            }

            // (2) A DJ jog wheel is offset-binary around 64 (rest 64, forward > 64, backward < 64). A jog
            // captured/shipped as two's-complement decoded 0x40 (rest) as -64 and flipped direction, so it
            // "threw unrelated positions and stuck in one spot". Rewrite any deck-slot DeckJog relative
            // binding still on two's-complement, keyed on the ACTION (not the CC) so a LEARNED jog — whose
            // CC differs from this default — is healed too.
            // ponytail: two's-complement DJ jogs don't exist in practice; the learn picker still offers it
            // for a rare encoder, and re-learning overwrites this heal.
            if (binding.Action == PerformanceActionKind.DeckJog
                && binding.InputMode == ActionInputMode.Relative
                && binding.Relative == RelativeEncoding.TwosComplement
                && binding.Slot is DeckASlot or DeckBSlot)
            {
                changed = true;
                binding = binding with { Relative = RelativeEncoding.OffsetBinary };
            }

            return binding;
        }).ToList();

        return changed ? profile with { Bindings = bindings } : profile;
    }

    /// <summary>
    /// Heals a profile saved while SYNC was a one-shot beatmatch up to the top-level sync lock:
    /// any deck-slot (0/1) <see cref="PerformanceActionKind.DeckSyncOnce"/> binding is rewritten to
    /// a toggle <see cref="PerformanceActionKind.DeckSyncToggle"/>, keeping the learned physical button.
    /// Other learned controls are left untouched.
    /// </summary>
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
