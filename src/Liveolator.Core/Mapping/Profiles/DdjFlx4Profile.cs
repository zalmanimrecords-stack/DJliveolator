using Liveolator.Core.Actions;

namespace Liveolator.Core.Mapping.Profiles;

/// <summary>
/// A default <see cref="ControllerMappingProfile"/> for the Pioneer DDJ-FLX4 (doc 05/07): a
/// 2-channel/2-deck DJ controller with two jog wheels, two tempo faders, two channel faders, a
/// crossfader, a 3-band EQ + a COLOR (filter) knob per channel, transport (play/cue) + beat-sync
/// buttons, and 8 performance pads per deck. It maps those controls to the existing deck/mixer/beat
/// <see cref="PerformanceActionKind"/>s so the hardware drives the one dispatcher.
/// </summary>
/// <remarks>
/// <para>
/// The note/CC numbers below follow Pioneer's published DDJ-400/FLX4 MIDI layout as a sensible
/// <b>default</b>, NOT a hardcoded device driver. Every binding is a plain
/// <see cref="ControllerBinding"/> the performer can override per control via
/// <see cref="MidiLearnSession"/>, and the <see cref="MappingConflictDetector"/> guards against
/// accidental collisions.
/// </para>
/// <para>
/// Convention: <b>Deck 1 = MIDI channel 0 / action slot 0</b>, <b>Deck 2 = MIDI channel 1 / action
/// slot 1</b>. The deck handler addresses decks by <see cref="PerformanceAction.Slot"/> and the mixer
/// EQ handler reads the band name from <see cref="PerformanceAction.Argument"/> (Low/Mid/High). The
/// performance pads sit on Pioneer's dedicated pad channels (Deck 1 = channel 7, Deck 2 = channel 9),
/// which is independent of the deck slot — the binding carries the deck via <c>Slot</c> and the
/// hot-cue index via <c>Argument</c>.
/// </para>
/// </remarks>
public static class DdjFlx4Profile
{
    /// <summary>The profile name persisted/shown in the Mappings UI.</summary>
    public const string ProfileName = "DDJ-FLX4 (Pioneer, default)";

    /// <summary>Substring matched against the device name to auto-select this profile (doc 05).</summary>
    public const string DeviceHint = "DDJ-FLX4";

    /// <summary>Hot-cue pads exposed by the default layout (matches the deck UI's hot-cue bank).</summary>
    public const int HotCuePadCount = 4;

    private const int Deck1Channel = 0;
    private const int Deck2Channel = 1;
    private const int Deck1Slot = 0;
    private const int Deck2Slot = 1;

    // Pioneer routes the performance pads on their own MIDI channels, distinct from the deck channel.
    private const int Deck1PadChannel = 7;
    private const int Deck2PadChannel = 9;

    // The crossfader and other shared (deck-agnostic) controls live on Pioneer's mixer channel.
    private const int MixerChannel = 6;

    // --- Default note numbers (transport/sync buttons) — learn-overridable. ---
    private const int PlayPauseNote = 0x0B;   // 11
    private const int CueNote = 0x0C;          // 12
    private const int SyncNote = 0x58;         // 88
    private const int HotCueBaseNote = 0x00;   // pads 1..4 → notes 0x00..0x03 on the pad channel

    // --- Default CC numbers (continuous controls) — learn-overridable. ---
    private const int TempoFaderCc = 0x00;     // per-deck pitch/tempo fader
    private const int ChannelFaderCc = 0x13;   // per-channel volume fader
    private const int EqHighCc = 0x07;
    private const int EqMidCc = 0x0B;
    private const int EqLowCc = 0x0F;
    private const int FilterCc = 0x17;         // COLOR knob (filter)
    private const int JogBendCc = 0x21;        // jog wheel pitch-bend (relative, offset-binary around 64)
    private const int CrossfaderCc = 0x1F;     // shared (deck-agnostic) crossfader, mixer channel
    private const double JogTicksPerRevolution = 128.0;

    /// <summary>The default DDJ-FLX4 mapping profile.</summary>
    public static ControllerMappingProfile Default { get; } = Build();

    private static ControllerMappingProfile Build()
    {
        var bindings = new List<ControllerBinding>();

        // Crossfader is deck-agnostic: one absolute fader on the mixer channel, slot 0 (the handler
        // applies it across both decks).
        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, MixerChannel, CrossfaderCc,
            PerformanceActionKind.MixerCrossfade, ActionInputMode.Absolute, Deck1Slot));

        AddDeck(bindings, Deck1Channel, Deck1PadChannel, Deck1Slot);
        AddDeck(bindings, Deck2Channel, Deck2PadChannel, Deck2Slot);

        return new ControllerMappingProfile(ProfileName, DeviceHint, bindings);
    }

    // Adds the per-deck strip: play/pause + cue + sync buttons, tempo fader, channel fader, 3-band EQ,
    // COLOR/filter, the jog-wheel nudge, and the hot-cue pads. Each deck lives on its own MIDI channel
    // so the same CC numbers can repeat across decks without colliding (the FLX4's mirrored layout).
    private static void AddDeck(
        List<ControllerBinding> bindings, int deckChannel, int padChannel, int slot)
    {
        bindings.Add(new ControllerBinding(
            MidiMessageType.NoteOn, deckChannel, PlayPauseNote,
            PerformanceActionKind.DeckPlayPause, ActionInputMode.Momentary, slot));

        bindings.Add(new ControllerBinding(
            MidiMessageType.NoteOn, deckChannel, CueNote,
            PerformanceActionKind.DeckCue, ActionInputMode.Momentary, slot));

        // SYNC = one-shot beatmatch (tempo + phase), momentary — a single press lines the deck up, then
        // the jog/tempo fader stay free for manual fine-tuning. Not a latch (no continuous loop), same
        // convention as the other shipped profiles.
        bindings.Add(new ControllerBinding(
            MidiMessageType.NoteOn, deckChannel, SyncNote,
            PerformanceActionKind.DeckSyncOnce, ActionInputMode.Momentary, slot));

        // Tempo fader → absolute pitch position (0..1, 0.5 = original tempo; the engine owns the % range).
        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, deckChannel, TempoFaderCc,
            PerformanceActionKind.DeckPitch, ActionInputMode.Absolute, slot));

        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, deckChannel, ChannelFaderCc,
            PerformanceActionKind.MixerChannelGain, ActionInputMode.Absolute, slot));

        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, deckChannel, EqHighCc,
            PerformanceActionKind.MixerEqBand, ActionInputMode.Absolute, slot, Argument: "High"));
        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, deckChannel, EqMidCc,
            PerformanceActionKind.MixerEqBand, ActionInputMode.Absolute, slot, Argument: "Mid"));
        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, deckChannel, EqLowCc,
            PerformanceActionKind.MixerEqBand, ActionInputMode.Absolute, slot, Argument: "Low"));

        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, deckChannel, FilterCc,
            PerformanceActionKind.MixerFilter, ActionInputMode.Absolute, slot));

        // The jog pitch-bend reports relative offset-binary steps centered on 64. Conversion normalizes
        // them to a fraction of a wheel revolution; DeckActionHandler then applies DJ-appropriate
        // playing/paused sensitivity.
        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, deckChannel, JogBendCc,
            PerformanceActionKind.DeckJog, ActionInputMode.Relative, slot,
            Relative: RelativeEncoding.OffsetBinary,
            RelativeTicksPerRevolution: JogTicksPerRevolution));

        // Performance pads (hot-cue mode): pads 1..4 → hot-cue indices 0..3. The deck rides in Slot; the
        // hot-cue index rides in Argument (the deck handler decides set-or-jump).
        for (int pad = 0; pad < HotCuePadCount; pad++)
        {
            bindings.Add(new ControllerBinding(
                MidiMessageType.NoteOn, padChannel, HotCueBaseNote + pad,
                PerformanceActionKind.DeckHotCue, ActionInputMode.Momentary, slot,
                Argument: pad.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }
}
