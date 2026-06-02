# 12 — UI Modules

## Purpose

Specify the new UI surfaces for Live Mode, fitting the existing analog-rack module
pattern. UI is a thin action source/feedback sink — it dispatches
`PerformanceAction`s (doc 04) and reflects feedback state; it holds no engine logic
(global standard #4).

## Visual language (chosen direction)

> Decided from the design prototypes in `design/mockups/` (dark analog-rack vs.
> vintage vs. Ableton-style). **Chosen: clean, flat, Ableton-Live-style.** The
> canonical reference is `design/mockups/live-mode-ableton.html`; the other skins
> are kept for record only. This supersedes the "analog-rack aesthetic" wording
> below — that text predates the cross-platform Avalonia move and is retained only
> for the module/feature map, not the look.

Principles (translate to Avalonia `Styles`/`ControlThemes` when building
`src/Liveolator.App`):

- **Flat & dense.** Neutral grays, dark 1px hairlines, ~2–3px corner radius, no
  gradients or drop shadows. Function over decoration.
- **Color = meaning.** Grays carry the chrome; saturated colors are reserved for
  clip/scene state and signal (selection yellow, play green, armed/clip orange,
  per-track clip colors). Never color-only — pair with text/icon (standard #25).
- **Typography.** Compact sans for labels/UI; monospace for numeric readouts
  (BPM, time, values).
- **Components.** Colored clip slots with a play triangle / stop square
  (Session-View grid), thin signal meters, ring-style encoders, minimal faders.

**Push 1 representation.** The performance surface mirrors the hardware on screen
so UI and pads/encoders stay in lockstep (one source of truth — dispatcher
feedback, doc 04):

- **Pads → Visual Scene Grid** as an 8×8 Session-View grid (clips + scene-launch
  column), see Module 4 below and doc 08.
- **8 encoders → a row of ring encoders** bound to visual macros
  (intensity / speed / echo / particles / kaleidoscope / zoom / hue / opacity,
  per doc 08). Plus the **Master + Swing** encoders. Encoders are an action
  source like any other (doc 04); the on-screen row reflects the same macro state
  the physical encoders drive (doc 06).

## Existing UI pattern this follows

`MilkDropVisualizer.App/UI.Analog/Windows/AnalogMainWindow.xaml.cs` hosts modules,
each a **ViewModel + Adapter** pair (e.g. `TapeDeckViewModel` + `TapeDeckAdapter`,
plus `SequenceTriggersModule`, `PatchBankModule`, `ModMatrixModule`,
`VisualChannelsModule`, `RenderOutputModule`). New modules follow the same shape and
live under `UI.Analog/Modules/`.

## Module 1 — DJ Sync / Beat Engine

Readout (binds to `BeatClockState`, doc 03):

```text
SOURCE: DECK / SYSTEM / INPUT
BPM: 128.02      CONF: 91%      LOCK: ON
PHASE: |----x-------|
BEAT: 137        BAR: 35.1
```

Controls (each dispatches an action, doc 04):

```text
AUTO   LOCK   TAP   /2   x2   NUDGE -   NUDGE +   SET DOWNBEAT   RESET GRID
```

- `LOCK`/`AUTO` reflect `ActionFeedbackState.IsActive` via `FeedbackChanged`.
- Source selector switches the active `IAudioSource` (doc 01).
- Tempo-candidate list (doc 03) is shown when confidence is low so the performer can
  pick the right BPM (half/double).

## Module 2 — Performance Mappings

Purpose:

- Select the MIDI input (and output/feedback) device (doc 05).
- Enable the Push 1 profile (doc 06) and the CMD STUDIO 2A profile (doc 07).
- Enter MIDI learn mode (pick action → move control → bind).
- Show current mapping **conflicts** (doc 05) so they can be resolved.
- Save / load / import / export mapping profiles (doc 13).
- Show device status / User-mode hint for Push 1 (doc 06) — "no MIDI received: is
  Push in User mode?"

### Audio device / backend picker

A small audio-device section (here or in the DJ Sync module) lets the performer:

- Choose the capture source backend: **WASAPI loopback** (system mix), **WASAPI
  input**, or **ASIO** (doc 01).
- Pick the concrete device / **ASIO driver** (`AsioOut.GetDriverNames()`), select
  input channels, and see reported latency.
- See a clear message when an ASIO driver is exclusively held by another app, with
  the WASAPI-loopback fallback offered.

## Module 3 — Tape Deck upgrade

Extends the existing Tape Deck (`TapeDeckViewModel`/`TapeDeckAdapter`) to bind the
live queue (doc 09):

- Live queue editing (reorder/insert/remove on `Upcoming`).
- Now / Next / Later grouping.
- Auto-play / autopilot status indicator.
- Safe skip modes: immediate, next beat, next bar.

This is an *extension*, not a redesign — existing transport controls and readouts are
preserved (global standard #12, no unnecessary UI change).

## Module 4 — Visual Scene Grid

- 8×8 scene grid mirroring the Push pad layout (doc 06).
- Scene slots show active / armed state (and "pending" while a quantized launch waits
  for the next bar).
- Bank switcher (selects the active `VisualBank`, doc 08).
- Per-slot beat-quantized launch setting (immediate / next beat / next bar).

The grid and Push stay in sync because both reflect the same dispatcher feedback
(doc 04) — the grid is just an on-screen mirror of the pads.

## Accessibility (global standard #25)

- All controls keyboard-reachable with visible focus states.
- Readouts have text labels (not color-only state); LOCK/AUTO show text + color.
- Sufficient contrast for the dark analog-rack theme.

## Error handling & logging

- The UI surfaces engine/device errors raised via events (capture loss, MIDI
  disconnect, missing preset) as non-blocking notifications; it never swallows them.
- Long operations (device enumeration) run off the UI thread to preserve the
  no-freeze guarantee (doc 00).

## Phase

- DJ Sync module: Phase 2 (consumes the beat engine).
- Performance Mappings module: Phase 5.
- Tape Deck upgrade: Phase 7.
- Visual Scene Grid: Phase 6 (alongside the Push profile).

## Risks

- The analog-rack aesthetic must absorb four new modules without crowding; consider a
  dedicated "Live" rack page/tab rather than stacking into the existing rack.
- The Scene Grid duplicating Push state risks drift; both must derive from one source
  of truth (the dispatcher feedback), never independent local state.
