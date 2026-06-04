# 11 — Deck A/B and DJ Playback Engine (Committed)

## Decision

**Confirmed by the user: Liveolator is the DJ player itself** (option A). It plays
both decks, mixes them (crossfader + per-channel gain/EQ/filter), and outputs the
master plus a headphone-cue feed through the CMD STUDIO 2A's built-in interface via
the low-latency output backend (ASIO on Windows / CoreAudio on macOS, doc 01). This
is a committed deliverable, not a deferred sketch.

It is still **sequenced last** (Phase 10) on purpose: it is the largest audio-engine
piece, and the single-deck core (docs 01–10) must be stable first so the deck work
builds on proven capture, beat, action, MIDI, and playlist layers — not the other way
around (global standard #10, small safe steps). Sequenced last ≠ optional.

## Purpose

A real two-deck DJ playback + mixing engine: independent transport per deck,
beatmatching via per-deck grids, a software mixer, and multi-channel output
(master + cue) over the cross-platform low-latency backend (doc 01).

Per the product direction (doc 00), this engine is tuned for **effortless** sync and
mixing — one-button tempo sync, separate phase snap, and an opt-in hands-free auto-mix —
so the performer's attention is freed for the visuals (see "Sync & beatmatching" and
"Auto-Mix" below).

## Existing code this touches

- `MilkDropVisualizer.App/Audio/AudioPlayer.cs` / `PlaylistAudioPlayer.cs` — today a
  single playback path, no second output, no mixing. Becomes the per-deck player
  (two instances) feeding a new mixer stage.
- `MilkDropVisualizer.App/Audio/AudioAnalyzer.cs` / the frame pipeline (doc 02) —
  analyzes the **master mix** (post-crossfader) for visuals. `DeckAudioSource`
  (doc 01) evolves into "master-mix source," so the beat engine gets the playing
  audio **directly** (no loopback capture needed when Liveolator is the player).
- The audio backend abstraction (doc 01): master + cue **output** routing uses the
  cross-platform low-latency backend (ASIO/WDM-KS on Windows, CoreAudio on macOS).
  Multi-channel **output** (master + cue) is now a confirmed requirement, not
  conditional.

## Hardware fit: CMD STUDIO 2A

Exactly matches this design:

- Two deck strips + crossfader + 2 channel faders + 3-band EQ + 8 hot cues per deck +
  touch jog wheels — the full control surface (doc 07).
- Built-in **4-channel** 24-bit interface → channels 1/2 = **master** (RCA),
  channels 3/4 = **headphone cue**, with independent cue volume — routed via the
  low-latency backend (doc 01) for low latency.

## Architecture

```text
Deck A (player + reader + pitch/EQ) ┐
                                    ├─> DeckMixer ──> Master bus ──> out ch 1/2 (RCA)
Deck B (player + reader + pitch/EQ) ┘     │            └─> frame pipeline (doc 02) → visuals
                                          └─> Cue bus (PFL per deck) ──> out ch 3/4 (headphones)

(output channels via the low-latency backend, doc 01: ASIO/WDM-KS on Windows, CoreAudio on macOS)
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
    // Master + cue rendered to the selected output channels via the backend (doc 01).
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

**Harmonic-mixing hint:** each track carries a precomputed `MusicalKey` / Camelot code
(doc 03 key detection, doc 13 persistence). When choosing the next track, the library
flags **harmonically compatible** candidates (±1 same letter, or same number / switched
letter) so the performer — or the auto-mix engine — can pick a clean key transition
without musical theory knowledge. This is a UI hint, never a hard constraint.

## Features in scope

- Deck A/B with independent transport, pitch fader, and per-deck beat grid.
- Crossfader with selectable curve.
- Per-deck 3-band EQ + filter.
- Hot cues / cue points (8 per deck on the CMD STUDIO 2A).
- Loops (beat-synced via each deck's grid).
- Headphone cue (PFL) and master output routing over the low-latency backend (doc 01).
- Beatmatching aids: per-deck BPM display, sync/nudge from jog wheels (doc 07).

## Sync & beatmatching (committed — was an open question, now decided)

The product direction (doc 00) makes **effortless sync** a requirement, so the sync model
is decided here rather than left open. It follows the Mixxx leader/follower reference
(see `docs/research/dj-gaps-keydetect-latency-automix-avsync.md`):

- **Sync Lock (one button) = tempo match.** Engaging Sync on a deck matches its tempo to
  the sync **leader** and keeps following tempo changes. It **auto-handles ½×/2× BPM**
  relationships (a 70 BPM track follows a 140 BPM leader without an out-of-range pitch).
- **Leader selection** priority: an **explicit** user-set leader wins; else a **playing /
  audible** deck; else the **internal clock** (the doc 03 beat timeline) as fallback.
- **Rate** of a follower deck: `rate = (leader_bpm / deck_base_bpm) × halve_double_factor`.
- **Quantize (separate control) = phase match.** Tempo sync alone does **not** align beats;
  Quantize snaps beats into phase. Phase uses the doc 03 **beat-distance** value `[0,1)`
  with proportional rate corrections **capped at ±5%**; phase correction runs **only when
  Quantize is on**. Keeping the two controls separate is intentional (tempo-locked but
  hand-nudged phase is a valid performance mode).
- **Manual beatmatch remains available** (pitch fader + jog nudge) for performers who want
  it — Sync is an assist, not a lock-out.
- All of the above are driven by `PerformanceAction`s (doc 04), so Push 1, the CMD STUDIO
  2A, the UI, and autopilot trigger identical behavior.

## Auto-Mix (assisted, hands-free — frees attention for visuals)

An **opt-in** mode that performs deck-to-deck transitions automatically so the operator can
focus on the visual performance (doc 00 differentiator; doc 10 autopilot is the natural
driver). Reference model = Mixxx Auto DJ + academic switch-point detection:

- **Per-track intro/outro cues** (Intro Start/End, Outro Start/End), with Intro Start /
  Outro End **auto-detected via silence detection**; reuses any cues the performer already
  set.
- **Transition timing** anchored to those cues and **clamped to musical boundaries** (ends
  at Intro End or Outro End, whichever comes first) — never an arbitrary mid-phrase cut.
- **Auto tempo-match** engages when the two BPMs are within ~6%; transition **style** is
  chosen from the two BPMs + the configured transition time.
- **Smarter cue placement (later):** "switch points" detected from rhythmic/loudness/timbre
  novelty, aligned to beats/downbeats/phrases (≈90–96% usable in evaluations) — a v2
  upgrade over silence-only cues.
- Auto-Mix drives the crossfader exclusively while engaged; the performer can take over at
  any time (consistent with the autopilot override model, doc 10).

## Open sub-questions to settle when scheduling Phase 10

- **Gapless/crossfade & true scratch** raise the audio bar (jog-wheel scratch implies
  real-time resampling/seek). Decide v1 scope: smooth crossfade first, scratch later.
- **EQ/filter DSP**: use the chosen audio library's filter chain (BiQuad) vs a dedicated
  DSP implementation — pick when implementing (depends on the doc 00 audio-library
  decision).

## Why still last in sequence

- Low-latency, multi-output, gapless audio with EQ/filter is its own large project.
- Every earlier phase (capture, beat, actions, MIDI, Push, single-deck playlist) is a
  prerequisite or lower-risk and higher-value-per-effort.
- Building decks first would slow the whole roadmap and risk the stable core.

## Error handling, persistence, testing

- Output device/channel selection and backend failures follow the surfaced-error /
  logged-fallback rules of doc 01 (never crash the render or audio loop).
- Per-deck state (loaded track, cue points, pitch) persists in the
  `LivePerformanceSession` (doc 13).
- Mixer math (crossfader curve, gain, EQ response) is pure and unit-tested in the
  xUnit project with known input buffers (doc 14); audio-device output is validated
  manually on the CMD STUDIO 2A.

## Phase

Phase 10 — committed. Success criterion: full two-deck DJ playback with master + cue
output on the CMD STUDIO 2A, without destabilizing the single-deck performance core.
