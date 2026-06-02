# 05 — Controller Mapping Engine

## Purpose

Translate MIDI/controller input into `PerformanceAction` (doc 04), with a learn
mode and savable mapping profiles. No controller code ever touches an engine
directly.

## Existing code this touches

None — there is **no MIDI infrastructure in the codebase today**. This is entirely
new, living under `MilkDropVisualizer.App/Live/Mapping/`.

## Library decision

**`Melanchall.DryWetMidi`** (NuGet, managed, MIT). Chosen for input *and* output
device handling and SysEx support (needed for Push LED feedback, doc 06). The
concrete library is hidden behind the abstractions below so it can be swapped.

```csharp
public interface IMidiInput : IDisposable
{
    string DeviceName { get; }
    bool IsOpen { get; }
    void Open();
    void Close();
    event EventHandler<MidiMessage>? MessageReceived;
}

public interface IMidiOutput : IDisposable     // LED / feedback
{
    string DeviceName { get; }
    void Send(MidiMessage message);
    void SendSysEx(ReadOnlyMemory<byte> data);
}

public sealed record MidiMessage(
    MidiMessageType Type,   // NoteOn, NoteOff, ControlChange, PitchBend
    int Channel,
    int Data1,              // note / cc number
    int Data2);             // velocity / cc value
```

`MidiInputService` / `MidiOutputService` wrap DryWetMidi devices and adapt to these
interfaces. Device enumeration is exposed for the Mappings UI (doc 12).

## Mapping model

```csharp
public sealed record ControllerBinding(
    MidiMessageType TriggerType,
    int Channel,
    int Data1,                       // note or CC number that triggers
    PerformanceActionKind Action,
    ActionInputMode InputMode,       // how Data2 is interpreted
    int Slot = 0,
    string? Argument = null,
    ValueCurve Curve = ValueCurve.Linear);   // for absolute/relative scaling

public sealed record ControllerMappingProfile(
    string Name,
    string DeviceHint,               // matches against device name
    IReadOnlyList<ControllerBinding> Bindings);
```

The mapping engine takes an incoming `MidiMessage`, finds the matching
`ControllerBinding`, converts `Data2` per `InputMode`/`Curve` into a
`PerformanceAction`, and hands it to the dispatcher.

```csharp
public interface IControllerMapper
{
    ControllerMappingProfile ActiveProfile { get; }
    void Apply(MidiMessage message);   // -> dispatcher
}
```

## Input type handling

- **Note on/off** → momentary or toggle actions (velocity available as value).
- **CC absolute** → `Absolute` value 0..1 (cc/127), with curve.
- **CC relative** → `Relative` deltas; support the common relative encodings
  (two's-complement / offset-64 / signed-bit) selectable per binding.
- **Pitch bend** → absolute bipolar value (tempo nudge, crossfader).
- **Transport-style buttons** → momentary actions.

## MIDI learn mode

```csharp
public interface IMidiLearnSession
{
    void Begin(PerformanceActionKind action, int slot = 0);
    // Captures the next inbound message, infers TriggerType/InputMode,
    // produces a ControllerBinding, and adds it to the working profile.
    event EventHandler<ControllerBinding>? Learned;
    void Cancel();
}
```

Learn flow: user picks an action in the UI → arms learn → moves a control → the
engine captures the message, infers note/CC and absolute/relative, and creates the
binding. Inference is heuristic (e.g. a control that sends a stream of changing CC
values is treated as absolute; a single note-on/off pair as momentary).

## Conflict detection

When two bindings share the same `(TriggerType, Channel, Data1)` they conflict. The
engine reports conflicts to the UI (doc 12) so the performer can resolve them; it
does not silently pick a winner (global standard #26).

## Feedback / output

The mapper subscribes to the dispatcher's `FeedbackChanged` (doc 04) and, for
bindings on feedback-capable devices, sends MIDI notes/CCs (or SysEx for Push) to
drive LEDs. Output is a separate concern (`IMidiOutput`) from input.

## Persistence

`ControllerMappingProfile` is JSON-serialized under the Live persistence root
(doc 13) and is import/export-friendly so profiles can be shared.

## Error handling & logging

- Opening/closing devices and the MIDI callback are wrapped in try/catch with the
  device name logged; device-disconnect mid-set is surfaced and the profile stays
  loaded for reconnect.
- Malformed/unmapped messages are ignored quietly at debug level (MIDI is chatty),
  but mapping failures (e.g. action dispatch threw) are logged with context.

## Phase

Phase 5 (MIDI input + learn): device enumeration, generic listener, mapping
profiles, learn mode, conflict surfacing, persistence.

## Risks

- Relative-encoder encodings vary by device; make the encoding explicit per binding
  and default to the most common, learnable via learn mode.
- Learn-mode inference can guess wrong (absolute vs relative); always let the user
  override the inferred `InputMode`.
- A flood of CC messages must not back up the dispatcher; coalesce rapid absolute
  updates per control if needed.
