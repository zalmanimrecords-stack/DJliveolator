# 07 — DJ Controller Profile

## Target device: Behringer CMD STUDIO 2A (confirmed)

The first real target is the **Behringer CMD STUDIO 2A** (user-confirmed). Verified
characteristics that shape this profile:

- **Dual-deck** layout (Deck A / Deck B) with two **touch-sensitive jog wheels**.
- Mixer section: **crossfader**, **2 channel faders**, **3-band EQ** per channel,
  **cue** buttons, library navigation + **track load** buttons, **8 hot cues**.
- **Class-compliant MIDI** (USB, plug-and-play) — works directly with DryWetMidi
  (doc 05), no vendor driver needed for the MIDI side.
- A **built-in 4-channel 24-bit USB audio interface**: stereo RCA master out + 1/8"
  TRS headphone out with independent volume. This is the "sound card" referenced in
  the requirements and the reason ASIO matters — see doc 01 (ASIO backend) and doc 11
  (output routing / headphone cue).

Because it is dual-deck with a built-in cue-capable interface, the CMD STUDIO 2A is
also the concrete hardware that justifies the Deck A/B + headphone-cue design in
doc 11 — though v1 of this profile targets the single-deck workflow first.

## Purpose

A `ControllerMappingProfile` (doc 05) for the CMD STUDIO 2A covering transport,
browsing, loading, and tempo control — so the controller drives the music while
Push 1 drives visuals. Generic learn-mode (doc 05) remains available for remapping.

## Existing code this touches

Builds on the mapping engine (doc 05), the action dispatcher (doc 04), and the live
playlist engine (doc 09). No DJ-controller-specific logic leaks into engines.

## v1 mapping (single deck, using Deck A of the CMD STUDIO 2A)

```text
Deck A Play/Pause -> TransportPlayPause
Deck A Cue        -> (reserved) future cue action
Library/browse    -> playlist selection (relative; moves selection cursor)
Load (Deck A)     -> TransportLoadSelectedTrack
Next / Previous   -> TransportNextTrack / TransportPreviousTrack
Jog wheel (slow)  -> BeatNudgeForward / BeatNudgeBackward  (tempo/phase nudge)
Tempo fader       -> (reserved) future tempo set
Hot cue 1         -> BeatTapTempo            (until a dedicated tap control is chosen)
```

The library/browse control uses `Relative` input (doc 04/05) to move the playlist
selection cursor in the Tape Deck (doc 12). Load commits the selected track per the
live playlist's Now/Next/Later model (doc 09). Exact note/CC numbers come from the
CMD STUDIO 2A MIDI implementation and/or learn mode — not hardcoded guesses here.

> The CMD STUDIO 2A has no dedicated "tap tempo" button, so v1 borrows a hot-cue pad
> for tap. This is a default the performer can remap via learn mode.

## CMD STUDIO 2A controls — full mapping (confirmed in scope)

Because the user confirmed **Zalmanolator is the DJ player** (doc 11), the full
control surface is a committed target (mapped in Phase 10, not "maybe later"):

- **Deck A/B** selection — two full deck strips already present.
- **Crossfader** + **2 channel faders** — mix/blend control.
- **3-band EQ** per channel — filter/EQ actions.
- **8 hot cues** — cue points / scene triggers.
- **Touch jog wheels** — scratch/seek (needs a seekable live source).
- **Headphone cue** via the built-in interface — see doc 11 output routing + doc 01
  ASIO.

These depend on the Deck A/B architecture (doc 11) and the ASIO output path (doc 01).
They are committed for Phase 10, but stay out of *this profile's v1* until the
single-deck performance core is solid.

## Generic-first stance

Because DJ controllers vary widely, the **generic MIDI learn mode (doc 05) is the
primary path**. This profile is a convenient default for common controllers, not a
device-specific driver. Performers learn-map their own controller on top of it.

## Profile import / export

`DjControllerProfile` (a named `ControllerMappingProfile`) supports JSON
import/export (doc 13) so performers can share controller layouts.

## Error handling & logging

Same as the mapping engine (doc 05): device open/close and message handling wrapped
with context logging; disconnects surfaced and recoverable.

## Phase

Phase 8 (DJ Controller Profile v1): play/pause, browse, load, next/previous, tap,
lock, nudge + import/export.

Success criteria (plan): a DJ controller operates the single-deck playlist workflow
while Push simultaneously controls visuals.

## Risks

- The exact CMD STUDIO 2A note/CC map must be confirmed from its MIDI implementation
  chart or captured via learn mode before shipping the default profile — do not
  hardcode guessed values.
- Jog-wheel scratch/seek semantics are intentionally excluded from v1 (they imply
  scrub/seek on a live source) — revisit with Deck A/B (doc 11).
- The built-in audio interface and ASIO are a separate concern (docs 01, 11) from the
  MIDI mapping here; keep them decoupled.

## Resolved

Target controller is the **Behringer CMD STUDIO 2A** (user-confirmed). The Phase 8
prerequisite in [15 — Phased roadmap](15-phased-roadmap.md) is satisfied. A capture of
its MIDI map (via learn mode) is the only remaining input needed to finalize the
default profile.
