---
name: add-controller-mapping
description: Map a hardware control (Push pad/button/knob or CMD STUDIO 2A control) to a PerformanceAction in Liveolator — via a ControllerBinding in a mapping profile, captured through MIDI learn rather than hardcoded CC numbers. Use when binding a new controller, adding a pad/knob/fader mapping, supporting a new device, or wiring MIDI input to an engine action.
---

# Map a controller to an action

In Liveolator, controllers never touch engines. Inbound MIDI flows:

```
MidiMessage → IControllerMapper.Apply → match ControllerBinding (from the active
ControllerMappingProfile) → IPerformanceActionDispatcher.Dispatch(action)
```

So "adding a mapping" means adding a `ControllerBinding`, **not** writing device code.

Authoritative design: [`docs/05`](../../../docs/05-controller-mapping-engine.md).
Real types: `src/Liveolator.Core/Mapping/`.

## Core rule: never hardcode CC/note numbers

CMD STUDIO 2A's MIDI map is captured via **learn mode**, not hardcoded (project
`AGENTS.md`). A binding is produced by `IMidiLearnSession`: arm an action, let the user
move the control, and the captured message is inferred into a `ControllerBinding`. The
inferred `InputMode` is always user-overridable.

## A binding (the unit you add)

`ControllerBinding` (`ControllerBinding.cs`) is a serializable record:
`TriggerType` (note / CC / pitch-bend) · `Channel` · `Data1` (note/CC number) ·
`Action` (the `PerformanceActionKind`) · `InputMode` · `Slot` · `Argument` ·
`Curve` (absolute scaling) · `Relative` (encoder encoding). Profiles are JSON —
they serialize and ship (doc 05/13).

## Steps (TDD-first)

1. **Write the test first** under `tests/Liveolator.Core.Tests/Mapping/` using the
   doubles in `MappingTestDoubles.cs`. Assert that:
   - a given `MidiMessage` matched against a profile dispatches the expected action
     with the right `Value`/`Slot`/`InputMode` (`BindingMatcher` / `ControllerMapper`);
   - for learn: arming an action + `Observe(message)` raises `Learned` with a binding
     whose `TriggerType`/`Channel`/`Data1` came from the message;
   - the absolute/relative value conversion (`ControlValueConverter`, `ValueCurve`,
     `RelativeEncoding`) produces the expected action `Value`.

2. **Confirm the target `PerformanceActionKind` exists.** If not, add it first with the
   `add-performance-action` skill — there is no point mapping to a kind no handler owns.

3. **Add the binding to a profile** (`ControllerMappingProfile`). Prefer producing it
   through `IMidiLearnSession` (the learn flow) over a literal `Data1`. Pick the
   `InputMode` that fits the control: button→`Momentary`/`Toggle`, fader/knob→`Absolute`,
   endless encoder→`Relative` (set `Relative`); set `Curve` for non-linear faders.

4. **Check for conflicts** with `MappingConflictDetector` — two controls bound to the
   same action, or one control bound twice, surface as `MappingConflict`. Resolve before
   saving the profile.

5. **Persist** the profile as JSON (doc 13). Device defaults (Push, doc 06) live as a
   shipped profile; user edits layer on top.

## Guardrails

- No `Data1`/CC constants scattered in code — bindings are data in profiles.
- The mapper only *translates and dispatches*; it must never call an engine directly.
- Unmatched messages are ignored quietly; malformed ones are handled, not thrown.

## Validate

```powershell
dotnet build
dotnet test
```
