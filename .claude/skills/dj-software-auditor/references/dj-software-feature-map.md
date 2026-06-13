# DJ Software Feature Map — the professional bar

The capability map of modern DJ software. For each area: what it is, the **pass bar** (what a
pro expects before trusting it live), common **failure modes**, and the **differentiation
edge** worth chasing. Use this to judge whether an implementation is competitive, not just
present.

Severity convention used throughout: **Critical** = unsafe to perform / data loss · **High**
= breaks a core workflow · **Medium** = friction · **Low** = polish.

---

## 1. Decks & playback

- **Pass bar:** instant load (<300 ms perceived), gapless start, accurate scrubbing, no pops
  on play/pause/cue, stable pitch over the full fader range, no drift over a 10-min track,
  correct end-of-track behavior, vinyl/CDJ-style platter feel where applicable.
- **Failure modes:** click/pop on cue, latency between hardware press and audio, pitch fader
  not zeroing cleanly, position readout drifting from audio, load stalling the UI thread,
  losing playhead on track reload.
- **Edge:** sub-50 ms total action→audio latency; visible, honest "loading/analyzing" state;
  4-deck without CPU cliff.

## 2. Beatgrids & BPM analysis

- **Pass bar:** auto-BPM within ±0.1 of truth for steady 4/4; downbeat on beat 1; **manual
  grid correction** (set first beat, shift, halve/double, tap, adjust whole-track vs from
  marker); handles variable-tempo and live/recorded material; grids persist with the track.
- **Failure modes:** half/double-time errors, downbeat off by a beat, grid that can't be
  fixed by hand, re-analysis silently overwriting hand edits, grid drift on long tracks
  (clock vs sample-rate mismatch).
- **Edge:** dynamic/elastic grids for non-quantized music; confidence indicator; bulk
  re-grid with edit protection.

## 3. Key detection & harmonic mixing

- **Pass bar:** key detection agreeing with reference (Mixed In Key-class accuracy) for most
  tracks; Camelot/Open Key notation; compatible-key suggestions (±1, relative, energy-boost
  +7); key shown in browser and on deck; key-lock independent of key display.
- **Failure modes:** wrong key on ambiguous/modal tracks with no override, no manual key
  correction, Camelot wheel math errors, no harmonic sort/filter.
- **Edge:** energy-aware harmonic suggestions; harmonic auto-playlist; key shift preview.

## 4. Waveforms, phrase detection, cues, loops, beat jump, slip

- **Pass bar:** scrollable + zoomable waveform, frequency-colored (low/mid/high), summary +
  detail views; cue point that's sample-accurate and audible-on-press; 8 hot cues with
  color/label, persisted; auto + manual loops, loop in/out, loop roll, halve/double loop
  length; beat jump (1/2/4/8...); slip mode that resumes the underlying playhead correctly;
  quantize so cues/loops snap to grid.
- **Failure modes:** waveform lagging the audio, loops not seamless (click at loop point),
  beat jump not beat-aligned, slip mode resuming at the wrong spot, hot cues lost on reload,
  quantize fighting intentional off-grid moves.
- **Edge:** automatic phrase/section detection (intro/verse/drop/outro) to guide transitions;
  active loop indicator; loop-resize while playing without artifacts.

## 5. Sync, tempo, pitch, master tempo / key-lock

- **Pass bar:** beat sync that locks phase (not just tempo), holds over minutes without
  drift, defines a clear tempo master, and lets you take manual control instantly; key-lock
  (master tempo) with minimal artifacts across a useful pitch range (±8% clean); pitch-bend /
  nudge for manual beatmatching.
- **Failure modes:** tempo-only "sync" that drifts in phase, sync that fights the user, no
  clear master, audible warble on key-lock, sync breaking on grid errors, sync re-snapping
  unexpectedly mid-transition.
- **Edge:** Ableton-Link-class shared clock across decks/devices/visuals; phase meter; smart
  master hand-off.

## 6. Library management

- **Pass bar:** fast browse of 50k+ tracks, crates/playlists, **smart playlists** (rule-based),
  multi-tag, fast incremental search, sortable/filterable columns (BPM, key, genre, rating,
  energy, comment, date added), history/session log, prepare/queue list, track coloring,
  "already played" marking.
- **Failure modes:** slow search/scroll at scale, no smart playlists, edits not persisting,
  no history, can't find a track fast under pressure.
- **Edge:** related-tracks suggestions, energy/mood columns, instant filter chips, fuzzy
  search.

## 7. Track import, metadata, formats, missing files, duplicates

- **Pass bar:** broad format support (MP3, AAC/M4A, WAV, AIFF, FLAC, ALAC, OGG); reads/writes
  ID3 + tags without corrupting files; relocate **missing files** in bulk; **duplicate
  detection**; import from other DJ apps (Rekordbox XML, Serato crates, iTunes/Music XML);
  handles drives going offline gracefully.
- **Failure modes:** silent skip of unsupported files, metadata corruption on write, no
  missing-file relocation, no dup detection, choking when a network/USB drive disappears.
- **Edge:** non-destructive metadata, library migration wizard, checksum-based dedup.

## 8. Analysis accuracy & edge cases

- **Pass bar:** consistent results across runs; correct on the hard cases (variable tempo,
  half-time feel, ambient/no-clear-beat, very short tracks, very long mixes, mono files,
  unusual sample rates); never blocks the UI; clearly marks unanalyzed/failed tracks; lets
  you re-analyze and protects manual edits.
- **Failure modes:** non-deterministic BPM, crashes/hangs on odd files, no failure state,
  re-analysis clobbering corrections.
- **Edge:** background batch analysis with progress + cancel; per-track confidence.

## 9. Mixer, EQ, gain staging, filters, effects, crossfader, routing

- **Pass bar:** 3-band EQ with full kill, clean gain structure (no clipping at unity, visible
  level meters), per-channel filter (LP/HP sweep), crossfader with selectable curves and
  channel assignment, FX with wet/dry + beat-synced timing, no zipper noise on any control,
  master/booth/headphone outputs.
- **Failure modes:** EQ not truly killing, gain clipping into the master, zipper noise,
  crossfader curve wrong/no options, FX not beat-locked, level meters absent or lying.
- **Edge:** isolator-quality EQ, FX chains/racks, send FX, color-FX per channel,
  parameter-locked FX on the beat.

## 10. Recording & broadcasting

- **Pass bar:** record the master to a clean file (WAV/AIFF/lossless option) without
  affecting playback; correct levels; split/cue points or per-track metadata; broadcast to an
  Icecast/Shoutcast/streaming endpoint with stable bitrate.
- **Failure modes:** recording dropping samples under load, level mismatch, no way to set
  format/path, broadcast disconnects without recovery.
- **Edge:** record while streaming, auto-tracklist export, loudness-normalized capture.

## 11. Controller, MIDI, HID, hardware integration

- **Pass bar:** class-compliant plug-and-play for major controllers; **MIDI learn**; HID for
  jog/platter where relevant; correct LED/screen feedback (button states, VU, position);
  low jog latency; hot-plug without restart; mapping profiles you can edit and share.
- **Failure modes:** hardcoded CC maps, no MIDI learn, laggy jogs, LED feedback out of sync
  with software state, crash on unplug, no profile management.
- **Edge:** Ableton-Control-Surface-style scripted mappings, per-device LED color models,
  takeover/soft-pickup for knobs, screen drawing on capable controllers.

## 12. Audio engine — performance, latency, stability, CPU

- **Pass bar:** low, stable latency via ASIO (Win) / CoreAudio (Mac); no dropouts at
  realistic buffer sizes with 2–4 decks + FX; bounded, predictable CPU; never blocks audio on
  UI/IO; graceful device-change handling; correct sample-rate conversion.
- **Failure modes:** dropouts/xruns under load, audio thread doing file IO or allocation,
  latency spikes, CPU climbing over a long set, crash/silence on device switch.
- **Edge:** real-time-safe engine, headroom telemetry, automatic buffer recommendations.

## 13. Streaming services & offline library

- **Pass bar:** if streaming is offered — clear licensing, caching for set reliability,
  graceful offline fallback, analysis of streamed tracks; offline library fully functional
  with no network.
- **Failure modes:** set breaking when wifi drops, streamed track not analyzed/grid-less,
  silent failures on auth expiry.
- **Edge:** pre-cache the prepared set, offline-first design.

## 14. Cloud sync & multi-device workflows

- **Pass bar:** library/crates/cues/grids/history sync across machines with conflict handling;
  clear sync status; works after offline edits.
- **Failure modes:** last-write-wins clobbering, cues/grids not syncing, no conflict UI,
  unclear what's synced.
- **Edge:** selective sync, version history, USB-export parity (Rekordbox-to-CDJ class).

## 15. Mobile / tablet support (where relevant)

- **Pass bar:** touch targets sized for performance, no accidental triggers, layout that
  survives a sweaty club, parity for the core moves it claims to support.
- **Failure modes:** tiny controls, mis-hits, no landscape, feature cliff vs desktop.
- **Edge:** tablet-as-controller, handoff with desktop.

## 16. Accessibility & keyboard workflows

- **Pass bar:** full keyboard control of core transport/cue/loop; visible focus; sufficient
  contrast (dark-room legible); screen-reader labels on controls; remappable shortcuts.
- **Failure modes:** mouse-only critical actions, low contrast, no focus states, fixed
  shortcuts.
- **Edge:** complete keyboard-only DJ workflow, high-contrast/colorblind-safe waveforms.

## 17. Onboarding & beginner UX

- **Pass bar:** a new DJ can load two tracks, beatmatch (with sync help), and crossfade within
  minutes; sane defaults; discoverable cue/loop; non-destructive exploration; helpful empty
  states.
- **Failure modes:** blank intimidating screen, hidden core actions, jargon with no guidance,
  destructive defaults.
- **Edge:** guided first-mix, contextual tips, "training wheels" sync that teaches.

## 18. Advanced / professional workflows

- **Pass bar:** fast prep (grids/cues/keys batchable), reliable hot-cue performance, loop
  rolls and beat jumps mapped to hardware, FX racks, 4-deck, set recording, history export,
  redundancy (backup, instant recovery).
- **Failure modes:** prep that doesn't scale, no hardware mapping for advanced moves, no
  redundancy story.
- **Edge:** stem separation, instant doubles, smart phrase-aware transitions, set automation.

## 19. Reliability under live conditions (cross-cutting — weight highest)

- **Pass bar:** no crashes in a multi-hour set; if something fails, audio keeps playing and
  the app recovers; autosave of library/cues; hot-plug survival; no modal dialogs that block
  the music; predictable behavior when a drive/controller/network drops.
- **Failure modes:** any crash, any audio dropout, blocking dialog mid-set, lost work,
  unrecoverable state.
- **Edge:** crash-resistant audio thread, live safety net (auto-resume, redundant deck),
  black-box logging for post-mortem.

---

## How to score an area

For each area rate: **Parity** (vs incumbents: Behind / At / Ahead), **Reliability** (live-safe?
yes/at-risk/no), **Effort to close gap** (S/M/L), **Priority** (Critical/High/Medium/Low).
Critical is reserved for anything that makes a live set unsafe or loses user data.
