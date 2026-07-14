# 06 — Ableton Push Profile

## Target device: Ableton Push 1 (confirmed)

The target is **Push 1** (the 2013 Ableton/Akai unit), confirmed by the user. This
fixes the LED/color model below. Push 2/3 differences are noted only where the
adapter would change.

## Purpose

A concrete `ControllerMappingProfile` (doc 05) plus device-specific LED/SysEx
feedback for the Ableton Push 1 — the performer's primary visual controller.

## Existing code this touches

Builds entirely on the mapping engine (doc 05) and the action dispatcher (doc 04).
No engine code is Push-aware; the profile is data + a device-specific feedback
adapter.

## Push v1 pad layout (8×8)

```text
Row 8  utility:  TAP  LOCK  /2  x2  BLACKOUT  AUTOPILOT  NEXT-TRACK  (spare)
Row 7  transitions / beat-quantized actions (TransitionNow / NextBeat / NextBar)
Row 6  overlays & effects  (ToggleOverlay per layer)
Row 5  overlays & effects
Rows 1-4  visual scenes / preset banks  (VisualLoadScene / TriggerPresetBank by slot)
```

Each pad maps to a `PerformanceAction` with a `Slot` index. Scene/bank pads use
`VisualLoadScene(slot)` / `VisualTriggerPresetBank(slot)` (doc 08). Utility pads map
to the matching beat/visual/transport actions.

## Knobs (Macro 1–8)

Mapped to `VisualSetMacro(name, value)` with `Absolute` input:

```text
1 intensity   2 speed        3 overlay scale   4 echo
5 particles   6 kaleidoscope 7 opacity         8 transition amount
```

Macro names align with the `VisualMacro` set in doc 08.

## Buttons

```text
Mode switch / Shift layer / Bank select / Visual lock / Playlist focus
```

- **Shift layer** lets a second action set share the pads (doubles capacity without
  more hardware). Implemented as a profile-level modifier that selects an alternate
  binding table while held.
- **Bank select** switches which `VisualBank` (doc 08) the scene rows address.

## LED feedback (Push 1 model)

Push 1 LED control is **mostly plain MIDI, not SysEx** — important to get right:

- **Pad LEDs (8×8, notes 36–99):** set with a **Note On** to the pad's note where
  **velocity = color palette index** (Push 1 has a fixed 0–127 color palette) and the
  **MIDI channel selects the animation** (channel 1 = solid; other channels =
  blink/pulse at fixed rates). So a pad is colored by sending NoteOn(note, velocity)
  on the right channel — no SysEx.
- **Button LEDs (top/bottom rows, CC):** set with a **CC** value (e.g. 0 off, dim,
  lit) — white/limited-color buttons.
- **LCD strip / palette / mode:** the only parts that use **SysEx** (text display,
  setting User mode).

Driven by the dispatcher's `FeedbackChanged` (doc 04) → `IMidiOutput` (doc 05):

- Scene/bank pads: dim = available, bright color = armed, blink channel = pending on
  a quantized launch (waiting for next bar).
- Utility toggles (LOCK, AUTOPILOT, BLACKOUT): on/off color reflects
  `ActionFeedbackState.IsActive`.

A `Push1FeedbackAdapter` encapsulates the note/CC/SysEx formatting and the color
palette so device-specific bytes stay in one file (global standards #2, #3). If
Push 2/3 support is added later, a sibling adapter handles their RGB-SysEx model
behind the same interface.

## User mode requirement

Push 1 must be in **User mode** (press the `User` button) for its pads/encoders to
send raw MIDI to Zalmanolator instead of driving Ableton Live. The Mappings UI
(doc 12) states this requirement and detects whether MIDI is arriving.

## Profile construction

The profile ships as a default `ControllerMappingProfile` (named `"Ableton Push v1"`,
`DeviceHint = "Push"`). The performer can clone and remap it via learn mode (doc 05);
defaults are never overwritten (doc 13 — user data separate from app defaults).

## Error handling & logging

- If the Push output port is unavailable, input still works; feedback degrades
  silently-but-logged (LEDs off), never blocking control.
- SysEx send failures are logged with context and do not interrupt the input path.

## Phase

Phase 6 (Push Profile v1): pads → scenes/banks, knobs → macros, basic LED feedback,
and TAP/LOCK/BLACKOUT available from Push.

Success criteria (plan): Push pads change visuals during playback; knobs control
visible macros; beat lock/tap/blackout available from Push.

## Risks

- The Push 1 color palette (velocity→color) is fixed and a little unintuitive; build a
  small palette constants table once and reuse it.
- If the performer forgets User mode, pads will drive Live (or nothing) and appear
  "dead" — the UI must catch and explain this.
- Profile defaults must never be overwritten by user remaps (doc 13).

## Resolved

Target device is **Push 1** (user-confirmed). The Phase 6 prerequisite in
[15 — Phased roadmap](15-phased-roadmap.md) is satisfied — no longer an open question.
