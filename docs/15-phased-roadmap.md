# 15 — Phased Roadmap

## Purpose

Tie every subsystem to a concrete, testable phase with explicit dependencies and the
existing files each phase touches. This is the build order.

## Dependency graph

```text
Phase 0 (seams) ─┬─> Phase 1 (capture) ─> Phase 2 (beat) ─> Phase 3 (visual sync)
                 │                                  │
                 └─> Phase 4 (actions) ─> Phase 5 (MIDI+learn) ─> Phase 6 (Push)
                                                    │
                                                    ├─> Phase 7 (live playlist)
                                                    ├─> Phase 8 (DJ controller)
                                                    └─> Phase 9 (autopilot)
                                                                  │
                                                                  └─> Phase 10 (decks, deferred)
```

Phases 4 (actions) and 1–2 (capture/beat) can proceed in parallel after Phase 0;
they converge at Phase 6.

## Phases

### Phase 0 — Design & refactor boundary  · doc 00
- Add the four seam interfaces (`IAudioSource`, `IAudioFrameProvider`, `IBeatClock`,
  `IPerformanceActionDispatcher`) with no behavior change.
- Document the current `AudioPlayer → AudioAnalyzer → ProjectMVisualizerHost` flow.
- **Touches:** new `Live/` folder; no existing logic changed.
- **Done when:** existing playback/visualization unchanged; build + xUnit green.

### Phase 1 — Live audio capture MVP  · docs 01, 02
- `SystemLoopbackAudioSource` + WASAPI device enumeration + ring buffer + signal
  meter.
- Extract `SpectrumAnalyzer` from `AudioAnalyzer`; route projectM feed through
  `AudioFramePipeline`.
- **Touches:** `Audio/AudioAnalyzer.cs` (split), `ProjectMVisualizerHost` (feed
  source), new `Live/Audio`, `Live/Frames`.
- **Done when:** system audio drives projectM with no file; deck still works; no UI
  freeze; pipeline regression test passes.

### Phase 1b — Sound card / ASIO  · doc 01
- WASAPI/ASIO backend abstraction + `IAudioDeviceCatalog`
  (`AsioOut.GetDriverNames()`) + `SoundCardInputAudioSource`.
- Audio device picker in the UI (doc 12); report ASIO latency.
- **Done when:** an ASIO interface (e.g. CMD STUDIO 2A) can be selected for capture;
  WASAPI fallback works when ASIO is exclusively held.

### Phase 2 — Beat engine v1  · docs 03, 12 (DJ Sync module)
- Onset/tempo/tracker/grid/clock + `BeatClockState`; tap/lock/÷2/×2/nudge.
- Replace `BpmDetector`; make `BeatDetectorService` a facade over the new clock.
- DJ Sync UI module.
- **Touches:** `Audio/BpmDetector.cs` (replaced), `Helpers/BeatDetectorService.cs`
  (facade), new `Live/Beat`, new `UI.Analog/Modules/DjSync*`.
- **Done when:** stable BPM on common tracks; confidence + candidates exposed; no
  false lock on silence; click-track tests pass.

### Phase 3 — Visual beat-clock integration  · doc 08
- Overlays/preset timing consume `BeatClockState`; `Quantize` helper; beat/bar-aware
  preset switching option.
- **Touches:** overlay/echo/particle engines, `ProjectMVisualizerHost` preset timing.
- **Done when:** visuals stable when locked; transitions launch on beat/bar; existing
  overlay beat behavior preserved or improved.

### Phase 4 — Performance action system  · doc 04
- `PerformanceAction` model + dispatcher + feedback; route `NextPreset`, `Blackout`,
  `TapTempo`, `LockBeat`, `NextTrack` through it.
- **Touches:** `UI.Analog` RelayCommands/handlers for those five actions; new
  `Live/Actions`.
- **Done when:** the five actions fire from a shared dispatcher with no direct
  controller→projectM coupling; dispatcher unit tests pass.

### Phase 5 — MIDI input & learn mode  · docs 05, 12 (Mappings module), 13
- DryWetMidi device enumeration, generic listener, mapping profiles, learn mode,
  conflict surfacing, persistence + `live/` storage layout.
- **Touches:** new `Live/Mapping`, new `UI.Analog/Modules/Mappings*`, persistence.
- **Done when:** any basic MIDI controller triggers visual actions; mappings persist;
  conflicts visible; mapping/learn tests pass.

### Phase 6 — Push profile v1  · docs 06, 08, 12 (Scene Grid)
- Push mapping (pads→scenes/banks, knobs→macros), LED feedback; `VisualScene`/`Bank`/
  `Macro`; Scene Grid UI.
- **Touches:** new `Live/Mapping/Profiles/Push*`, `Live/Visual`, `UI.Analog/Modules/
  SceneGrid*`.
- **Done when:** Push pads change visuals; knobs move macros; tap/lock/blackout from
  Push. **Target:** Push 1 (confirmed, doc 06).

### Phase 7 — Live playlist / assisted performance  · docs 09, 12 (Tape Deck upgrade)
- Now/Next/Later queue, reorder-while-playing, insert-next, remove-future, safe skip
  on beat/bar, preload.
- **Touches:** `Audio/PlaylistAudioPlayer.cs` (wrapped), `TapeDeckViewModel`/
  `TapeDeckAdapter` (extended), new `Live/Playlist`.
- **Done when:** auto-play runs while the upcoming list is edited live with no
  interruption; queue-mutation tests pass.

### Phase 8 — DJ controller profile v1  · doc 07
- CMD STUDIO 2A profile, single-deck workflow (play/pause, browse, load, next/prev,
  tap, lock, nudge) + import/export.
- **Touches:** new `Live/Mapping/Profiles/DjController*`.
- **Done when:** the CMD STUDIO 2A runs the single-deck workflow while Push controls
  visuals. **Target:** Behringer CMD STUDIO 2A (confirmed, doc 07); capture its MIDI
  map via learn mode.

### Phase 9 — Autopilot show rules  · doc 10
- Rule engine (beat/bar/energy/confidence/position), scene pools, cooldowns,
  intensity limits, seeded randomness, override semantics.
- **Touches:** new `Live/Autopilot`.
- **Done when:** unattended show runs over a playlist; manual override is instant.
  **Override:** defaults to auto-resume after a window (confirmed default, doc 10).

### Phase 10 — Deck A/B & DJ playback engine  · doc 11 (committed)
- **Confirmed: Zalmanolator is the DJ player.** Two-deck playback, software mixer
  (crossfader + per-deck gain/EQ/filter), hot cues, loops, beatmatching, and
  multi-channel **ASIO output** (master + headphone cue) on the CMD STUDIO 2A.
- Per-deck beat grids (doc 03) for sync; master mix feeds the frame pipeline (doc 02)
  and the beat engine directly.
- Sequenced last so the single-deck core is stable first — committed, not optional.
- **Done when:** full two-deck playback with master + cue output works on the CMD
  STUDIO 2A without destabilizing the core.

## Recommended MVP (plan's milestone, Phases 0–7 subset)

1. System audio live capture (P1)
2. projectM reacts to live system audio (P1)
3. Beat clock: BPM, confidence, phase, lock, tap, ÷2, ×2, nudge (P2)
4. Small DJ Sync UI module (P2)
5. Performance action dispatcher (P4)
6. Generic MIDI input with learn mode (P5)
7. Push pads → visual scenes / preset banks (P6)
8. Auto-play playlist editable for future tracks (P7)

## Decisions (all resolved)

| Question | Doc | Affects | Decision |
|----------|-----|---------|----------|
| Which Push model? | 06 | Phase 6 LED model | **Push 1** (confirmed) |
| Which DJ controller? | 07 | Phase 8 profile | **Behringer CMD STUDIO 2A** (confirmed); capture its MIDI map via learn mode |
| Autopilot override: pause vs auto-resume? | 10 | Phase 9 state machine | default **auto-resume** (both modes built; revisit anytime) |
| Deck A/B vs external-DJ-app loopback? | 11 | Phase 10 scope | **Zalmanolator is the DJ player** (Deck A/B committed) |

The only items still requiring real hardware before their phase: capturing the CMD
STUDIO 2A MIDI map (learn mode, Phase 8) and confirming the CMD STUDIO 2A ASIO
channel layout (Phase 1b / 10). These are validation steps, not design decisions.

## Sound card / ASIO requirement

Per the requirements, the audio layer must support real sound cards including **ASIO**
(the CMD STUDIO 2A's built-in 4-channel interface is the concrete case). Two parts:

- **Phase 1b** (doc 01) — ASIO/WASAPI **input/capture** backend abstraction,
  `IAudioDeviceCatalog` with `AsioOut.GetDriverNames()`, `SoundCardInputAudioSource`.
- **Phase 10** (doc 11) — multi-channel ASIO **output** routing (master ch 1/2 +
  headphone cue ch 3/4) for the DJ playback engine. **Confirmed required** now that
  Zalmanolator is the player.

## Cross-cutting (every phase)

- Persistence/versioning (doc 13) as types land.
- Tests-first in the xUnit pure-logic project (doc 14); validation gate before each
  phase is called done.
- Master **Live Mode** feature flag keeps the app behaving as today when off (doc 00).
