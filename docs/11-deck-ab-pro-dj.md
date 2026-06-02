# 11 — Deck A/B and DJ Playback Engine (Committed)

## Decision

**Confirmed by the user: Zalmanolator is the DJ player itself** (option A). It plays
both decks, mixes them (crossfader + per-channel gain/EQ/filter), and outputs the
master plus a headphone-cue feed through the CMD STUDIO 2A's built-in interface via
ASIO. This is a committed deliverable, not a deferred sketch.

It is still **sequenced last** (Phase 10) on purpose: it is the largest audio-engine
piece, and the single-deck core (docs 01–10) must be stable first so the deck work
builds on proven capture, beat, action, MIDI, and playlist layers — not the other way
around (global standard #10, small safe steps). Sequenced last ≠ optional.

## Purpose

A real two-deck DJ playback + mixing engine: independent transport per deck,
beatmatching via per-deck grids, a software mixer, and multi-channel ASIO output
(master + cue).

## Existing code this touches

- `MilkDropVisualizer.App/Audio/AudioPlayer.cs` / `PlaylistAudioPlayer.cs` — today a
  single playback path, no second output, no mixing. Becomes the per-deck player
  (two instances) feeding a new mixer stage.
- `MilkDropVisualizer.App/Audio/AudioAnalyzer.cs` / the frame pipeline (doc 02) —
  analyzes the **master mix** (post-crossfader) for visuals. `DeckAudioSource`
  (doc 01) evolves into "master-mix source," so the beat engine gets the playing
  audio **directly** (no loopback capture needed when Zalmanolator is the player).
- The audio backend abstraction (doc 01): master + cue **output** routing uses the
  WASAPI/ASIO backend. ASIO **output** is now a confirmed requirement, not
  conditional.

## Hardware fit: CMD STUDIO 2A

Exactly matches this design:

- Two deck strips + crossfader + 2 channel faders + 3-band EQ + 8 hot cues per deck +
  touch jog wheels — the full control surface (doc 07).
- Built-in **4-channel** 24-bit interface → channels 1/2 = **master** (RCA),
  channels 3/4 = **headphone cue**, with independent cue volume — routed via **ASIO**
  (doc 01) for low latency.

## Architecture

```text
Deck A (player + reader + pitch/EQ) ┐
                                    ├─> DeckMixer ──> Master bus ──> ASIO ch 1/2 (RCA)
Deck B (player + reader + pitch/EQ) ┘     │            └─> frame pipeline (doc 02) → visuals
                                          └─> Cue bus (PFL per deck) ──> ASIO ch 3/4 (headphones)
```

```csharp
public interface IDeck
{
    string Id { get; }                 // "A" / "B"
    ILivePlaylist Library { get; }      // track loading (see "Playlist model" below)
    IBeatClock BeatClock { get; }       // per-deck grid for beatmatching
    double Gain { get; set; }
    double PitchPercent { get; set; }   // tempo fader; affects this deck's grid
    EqBands Eq { get; set; }            // low/mid/high
    double FilterCutoff { get; set; }   // single-knob filter
    bool CueEnabled { get; set; }       // PFL → headphone bus
    DeckTransport Transport { get; }    // play/pause/cue/seek/hotcue/loop
}

public interface IDeckMixer
{
    IDeck A { get; }
    IDeck B { get; }
    double Crossfade { get; set; }      // -1 (A) .. +1 (B)
    CrossfaderCurve Curve { get; set; } // sharp/smooth
    IAudioSource MasterSource { get; }  // feeds the frame pipeline (doc 01/02)
    // Master + cue rendered to the selected ASIO output channels (doc 01).
}
```

The mixer mixes float PCM from both deck readers each audio buffer, applies
per-deck gain/EQ/filter and the crossfader curve, writes the **master** to the
output and to the frame pipeline, and writes per-deck **PFL** to the cue bus.

## Playlist model with two decks

The Now/Next/Later live queue (doc 09) becomes a **shared library/crate** the
performer loads tracks from onto either deck (Load A / Load B), rather than a single
auto-advancing queue. Auto-advance/assisted mode still applies per deck. This is a
refinement of doc 09 for the dual-deck case, decided when this phase is scheduled.

## Features in scope

- Deck A/B with independent transport, pitch fader, and per-deck beat grid.
- Crossfader with selectable curve.
- Per-deck 3-band EQ + filter.
- Hot cues / cue points (8 per deck on the CMD STUDIO 2A).
- Loops (beat-synced via each deck's grid).
- Headphone cue (PFL) and master output routing over ASIO.
- Beatmatching aids: per-deck BPM display, sync/nudge from jog wheels (doc 07).

## Open sub-questions to settle when scheduling Phase 10

- **Gapless/crossfade & true scratch** raise the audio bar (jog-wheel scratch implies
  real-time resampling/seek). Decide v1 scope: smooth crossfade first, scratch later.
- **EQ/filter DSP**: use NAudio's `ISampleProvider` chain (BiQuad filters) vs a
  dedicated DSP — pick when implementing.
- **Sync model**: master-deck sync vs free tempo with manual beatmatch.

## Why still last in sequence

- Low-latency, multi-output, gapless audio with EQ/filter is its own large project.
- Every earlier phase (capture, beat, actions, MIDI, Push, single-deck playlist) is a
  prerequisite or lower-risk and higher-value-per-effort.
- Building decks first would slow the whole roadmap and risk the stable core.

## Error handling, persistence, testing

- Output device/channel selection and ASIO failures follow the surfaced-error /
  logged-fallback rules of doc 01 (never crash the render or audio loop).
- Per-deck state (loaded track, cue points, pitch) persists in the
  `LivePerformanceSession` (doc 13).
- Mixer math (crossfader curve, gain, EQ response) is pure and unit-tested in the
  xUnit project with known input buffers (doc 14); audio-device output is validated
  manually on the CMD STUDIO 2A.

## Phase

Phase 10 — committed. Success criterion: full two-deck DJ playback with master + cue
output on the CMD STUDIO 2A, without destabilizing the single-deck performance core.
