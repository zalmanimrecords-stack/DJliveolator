using Liveolator.Core.Actions;

namespace Liveolator.Core.Mapping.Profiles;

/// <summary>
/// A default <see cref="ControllerMappingProfile"/> for the Ableton Push 1 (doc 06): the performer's
/// primary <b>visual</b> controller. Its 8x8 pad grid loads visual scenes, its eight macro encoders
/// drive named visual macros, and a row of utility buttons fires beat/visual/transition actions. It
/// maps those controls to the existing <see cref="PerformanceActionKind"/>s so the hardware drives the
/// one dispatcher (doc 04), exactly like the DJ-controller profiles.
/// </summary>
/// <remarks>
/// <para>
/// The note/CC numbers below follow Ableton's published Push 1 User-mode layout as a sensible
/// <b>default</b>, NOT a hardcoded device driver. Every binding is a plain <see cref="ControllerBinding"/>
/// the performer can override per control via <see cref="MidiLearnSession"/>, and the
/// <see cref="MappingConflictDetector"/> guards against accidental collisions. Push 1 must be in
/// <b>User mode</b> (press the User button) for its pads/encoders to send raw MIDI instead of driving
/// Ableton Live (doc 06).
/// </para>
/// <para>
/// Conventions the handlers rely on: scene pads carry the target scene index in
/// <see cref="ControllerBinding.Slot"/> (<see cref="PerformanceActionKind.VisualLoadScene"/> addresses a
/// scene by Slot); encoders carry the macro name in <see cref="ControllerBinding.Argument"/>
/// (<see cref="PerformanceActionKind.VisualSetMacro"/> reads it). Pad LEDs are driven back out via NoteOn
/// (velocity = color), button LEDs via CC, and LCD/User-mode via SysEx  -  that byte formatting lives in
/// <c>Liveolator.Midi</c> (<c>Push1Sysex</c>), not here.
/// </para>
/// <para>
/// Auto-selection by device name is intentionally NOT wired into the service container here; that is
/// owned elsewhere. <see cref="DeviceHint"/> ("Push") is provided so the selector can pick this profile,
/// matching the other shipped profiles.
/// </para>
/// </remarks>
public static class Push1Profile
{
    /// <summary>The profile name persisted/shown in the Mappings UI.</summary>
    public const string ProfileName = "Ableton Push 1 (default)";

    /// <summary>Substring matched against the device name to auto-select this profile (doc 05/06).</summary>
    public const string DeviceHint = "Push";

    /// <summary>Pads in the 8x8 grid (and the count of distinct visual scene slots they load).</summary>
    public const int PadCount = 64;

    /// <summary>Macro encoders across the top of the unit.</summary>
    public const int EncoderCount = 8;

    // Push 1 sends on a single MIDI channel in User mode (channel 1 = index 0). Pad-LED blink uses
    // OTHER channels on the OUTPUT path (doc 06 / Push1Sysex); inbound presses all arrive on channel 0.
    private const int PushChannel = 0;

    // --- Pad grid (8x8): Push 1 pads are NoteOn notes 36..99, bottom-left = 36. Pad index 0..63 maps
    //     to note 36 + index, and to VisualLoadScene(slot = pad index). Learn-overridable. ---
    private const int PadBaseNote = 36;

    // --- Macro encoders 1..8: Push 1's top encoders are CCs 71..78 in User mode. Each drives one named
    //     visual macro (doc 06 / doc 08 macro set). Learn-overridable. ---
    private const int EncoderBaseCc = 71;

    // The eight macro names, in encoder order (doc 06). Aligned with the VisualMacro vocabulary (doc 08).
    private static readonly string[] EncoderMacroNames =
    {
        "Intensity",
        "Speed",
        "OverlayScale",
        "Echo",
        "Particles",
        "Kaleidoscope",
        "Opacity",
        "TransitionAmount",
    };

    // --- Utility / transition buttons (CC, button LEDs are CC-lit on Push 1). Documented User-mode CCs;
    //     all learn-overridable. ---
    private const int TapTempoCc = 3;      // Push "Tap Tempo" button
    private const int MetronomeCc = 9;     // reused as beat-grid LOCK (toggle) by default
    private const int HalfTempoCc = 14;    // left "tempo" encoder-area button area (best-effort default)
    private const int DoubleTempoCc = 15;
    private const int BlackoutCc = 60;     // a dedicated utility button (best-effort default)
    private const int TransitionNowCc = 61;
    private const int TransitionNextBeatCc = 62;
    private const int TransitionNextBarCc = 63;

    /// <summary>The default Ableton Push 1 mapping profile.</summary>
    // Ableton Push 1 set-mode SysEx (header F0 47 7F 15, command 62, len 00 01, mode byte, F7): the Push
    // emits MIDI for its pads/encoders only in User mode, so the profile switches it on connect and back
    // to Live mode on disconnect (doc 06). Bytes mirror Push1Sysex.SetUserMode (separate feedback adapter).
    // Declared BEFORE Default so these initializers run before Build() reads them (textual static order).
    private static readonly byte[] UserModeOn = { 0xF0, 0x47, 0x7F, 0x15, 0x62, 0x00, 0x01, 0x01, 0xF7 };
    private static readonly byte[] LiveModeOn = { 0xF0, 0x47, 0x7F, 0x15, 0x62, 0x00, 0x01, 0x00, 0xF7 };

    public static ControllerMappingProfile Default { get; } = Build();

    private static ControllerMappingProfile Build()
    {
        var bindings = new List<ControllerBinding>();

        AddScenePads(bindings);
        AddMacroEncoders(bindings);
        AddUtilityButtons(bindings);

        return new ControllerMappingProfile(ProfileName, DeviceHint, bindings)
        {
            ActivationSysEx = UserModeOn,
            DeactivationSysEx = LiveModeOn,
            UsesColorFeedback = true, // Push pads are colour-addressed by NoteOn velocity (doc 06)
        };
    }

    // The 8x8 grid loads visual scenes: each pad press fires VisualLoadScene with the scene slot equal
    // to the pad index, so the handler resolves a scene purely from Slot (no Argument needed).
    private static void AddScenePads(List<ControllerBinding> bindings)
    {
        for (int pad = 0; pad < PadCount; pad++)
        {
            bindings.Add(new ControllerBinding(
                MidiMessageType.NoteOn, PushChannel, PadBaseNote + pad,
                PerformanceActionKind.VisualLoadScene, ActionInputMode.Momentary, Slot: pad));
        }
    }

    // Each top encoder drives one named visual macro absolutely (0..1). The macro name rides in
    // Argument; VisualSetMacro applies it to the active visual engine.
    private static void AddMacroEncoders(List<ControllerBinding> bindings)
    {
        for (int encoder = 0; encoder < EncoderCount; encoder++)
        {
            bindings.Add(new ControllerBinding(
                MidiMessageType.ControlChange, PushChannel, EncoderBaseCc + encoder,
                PerformanceActionKind.VisualSetMacro, ActionInputMode.Absolute,
                Argument: EncoderMacroNames[encoder]));
        }
    }

    // The utility row: beat-clock controls + visual blackout + the three quantized transitions. Buttons
    // are momentary presses; LOCK is the one latch (a toggle) since it gates the beat grid on/off.
    private static void AddUtilityButtons(List<ControllerBinding> bindings)
    {
        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, PushChannel, TapTempoCc,
            PerformanceActionKind.BeatTapTempo, ActionInputMode.Momentary));

        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, PushChannel, MetronomeCc,
            PerformanceActionKind.BeatLock, ActionInputMode.Toggle));

        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, PushChannel, HalfTempoCc,
            PerformanceActionKind.BeatHalfTempo, ActionInputMode.Momentary));

        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, PushChannel, DoubleTempoCc,
            PerformanceActionKind.BeatDoubleTempo, ActionInputMode.Momentary));

        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, PushChannel, BlackoutCc,
            PerformanceActionKind.VisualBlackout, ActionInputMode.Momentary));

        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, PushChannel, TransitionNowCc,
            PerformanceActionKind.VisualTransitionNow, ActionInputMode.Momentary));

        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, PushChannel, TransitionNextBeatCc,
            PerformanceActionKind.VisualTransitionNextBeat, ActionInputMode.Momentary));

        bindings.Add(new ControllerBinding(
            MidiMessageType.ControlChange, PushChannel, TransitionNextBarCc,
            PerformanceActionKind.VisualTransitionNextBar, ActionInputMode.Momentary));
    }
}
