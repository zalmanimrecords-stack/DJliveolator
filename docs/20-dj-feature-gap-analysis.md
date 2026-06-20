# 20 — DJ Feature Gap Analysis

> **⚠️ Point-in-time snapshot (2026-06-06) — partially superseded.** Several rows below have
> since been built; for current code status always trust `docs/18-implementation-status.md`
> (the living map). Notably built *after* this audit: a real beatgrid / downbeat anchor
> (`src/Liveolator.Core/Analysis/Bpm/BeatGrid.cs`, `DownbeatEstimator.cs`) and kick-onset
> phase lock (`src/Liveolator.Core/Beat/OnsetPhaseLock.cs`) — rows 2.2 / 2.5; a master
> limiter (`src/Liveolator.Core/Dsp/MasterLimiter.cs`) — row 3.8; and structural / auto
> hot-cue detection (`src/Liveolator.Core/Analysis/Cues/StructuralCueDetector.cs`) — row 1.3.
> The architectural analysis still stands; treat the individual ✅ / 🟡 / 🔴 cells as historical.

> **Author's lens:** written as a working club/festival DJ + DSP engineer auditing
> Liveolator strictly as a **DJ application** (the VJ side is out of scope here except
> where it intersects). Sources: design docs 00–18 and the actual code under `src/`,
> cross-checked against `docs/18-implementation-status.md` so "designed-only" is kept
> distinct from "built" and from "not addressed." Date: 2026-06-06.

---

## 1. Executive summary

Liveolator has an unusually clean *architecture* for a DJ app — the action/dispatcher
seam (`Liveolator.Core/Actions`), a shared Link-style beat timeline
(`Liveolator.Core/Beat/BeatTimeline.cs`), pure-and-tested mixer DSP
(`Liveolator.Core/Mixer/MixerMath.cs`), and a real two-deck BASSmix engine
(`src/Liveolator.Audio/Playback/TwoDeckBassEngine.cs`) that already plays two streams,
crossfades, applies 3-band EQ + single-knob filter in managed DSP, and feeds the
post-crossfader master into the beat clock. That is a genuine, working mixing core, not
a sketch. But measured against what a *standard professional DJ app* must do, the engine
is still **early-alpha**: it has **no looping, no cue points beyond "jump to start," no
keylock/master-tempo, no real beatgrid (downbeat/phase anchor), no phase sync (Quantize
is a latched no-op), no headphone-cue output, no end-of-track handling, and no scratch/jog
**. Crucially, the project deliberately scopes itself *down* (doc 00: "deliberately not a
maximalist pro-DJ tool" — sync/mix should be near-automatic so attention goes to visuals),
so some pro features are intentional non-goals. Even within that reduced ambition, however,
the **single most important gap-cluster to close first is the beatgrid → phase-sync → loop
chain**: without a per-track first-beat/downbeat anchor, the product's *headline promise*
("effortless one-button sync") is only half-built — tempo matches but beats do not align,
which to any DJ's ear is "not synced." Everything from Quantize to auto-mix to beat-synced
visuals depends on that anchor existing. Close that, add a headphone-cue bus, add loops and
real cue points, and Liveolator becomes a usable (if minimal) DJ app; until then it is a
two-deck tempo-matched blender.

---

## 2. Coverage matrix

Status key: ✅ Built (in code + tested) · 🟡 Designed-only (doc exists, no/partial code) ·
🔴 Missing (not built and not meaningfully designed) · ⚪ N/A or out-of-scope by product decision.

| # | DJ feature | Status | Note | Doc / code reference |
|---|-----------|--------|------|----------------------|
| **Decks & transport** |
| 1.1 | Play / pause / stop | ✅ | Per-slot, through dispatcher. | `TwoDeckBassEngine.PlayPause/Stop`, `DeckActionHandler` |
| 1.2 | Temporary cue (CDJ-style "back-to-cue") | 🟡 | `Cue` jumps to **track start** + pauses; not a settable cue, no cue-play-hold. | `TwoDeckBassEngine.Cue` (line 229) |
| 1.3 | Hot cues | 🟡 | 8/deck, set/jump by position fraction, **memory-only** — not persisted, not on the beatgrid, no per-pad LED feedback, lost on reload. | `TwoDeckBassEngine.HotCue`, doc 18 |
| 1.4 | Loops (manual in/out, auto/beat loops, loop roll) | 🔴 | **Not implemented.** `DeckSetLoop` kind exists; handler does not handle it; engine has no loop. Blocked on a per-deck runtime beat length. | `PerformanceActionKind.DeckSetLoop` (declared, unhandled), doc 11 "Open sub-questions" |
| 1.5 | Needle / track seek | ✅ (engine) / 🟡 (UI) | Absolute + relative seek in engine; UI playhead is read-only, **no click-to-seek on waveform**. | `TwoDeckBassEngine.Seek`, `DeckViewModel.Progress`, doc 18 (deferred) |
| 1.6 | Vinyl mode / scratch / jog | 🔴 | Explicitly deferred; jog-wheel scratch needs realtime resample/scrub. | doc 07 risks, doc 11 "Open sub-questions" |
| 1.7 | Keylock / master tempo (tempo change w/o pitch) | 🔴 | Pitch is **vinyl-style** (frequency scaling — tempo+pitch move together). No BASS_FX, so no keylock. | `BassMixerBackend.SetDeckRate` (line 124), doc 18 deferred |
| **Tempo & sync** |
| 2.1 | Offline BPM detection | ✅ | Spectral-flux onset → autocorrelation; tested vs synthetic click tracks. | `OnsetEnvelope.cs`, `TempoEstimator.cs`, `BpmDetector.cs` |
| 2.2 | Beatgrid (downbeat/phase anchor, first-beat offset) | 🔴 | **No `BeatGrid` type exists in code.** `TrackAnalysisResult` carries `BpmResult` only — BPM + confidence, **no offset**. Doc 16's `TrackAnalysis.Grid` is unbuilt. | `TrackAnalyzer.cs` (line 7), grep: no `BeatGrid` in `src/` |
| 2.3 | Manual beatgrid edit/adjust | 🔴 | Depends on 2.2; doc 13 designs `IsManual` protection but nothing to protect yet. | doc 13 §3 (designed-only) |
| 2.4 | Beat sync (tempo match, ½×/2× fold) | ✅ | `TempoSyncCalculator` folds octaves; engine re-applies on load/pitch/leader change. | `TempoSyncCalculator.cs`, `TwoDeckBassEngine.ReapplyRate` |
| 2.5 | Phase sync / Quantize (beat alignment) | 🟡 | **Latched no-op** — flag held + fed back, but `SetQuantize` logs "not yet implemented." Needs the 2.2 anchor. | `TwoDeckBassEngine.SetQuantize` (line 326) |
| 2.6 | Tempo range / pitch fader | ✅ | ±8% fixed range, per-slot, persists across loads. | `TwoDeckBassEngine` `PitchRangePercent` (line 31) |
| 2.7 | Pitch bend / nudge (jog or buttons) | 🟡 | `BeatNudge±` exists for the **beat clock**; no per-deck temporary pitch-bend (the DJ "nudge to push the beat") on the audio. | `BeatActionHandler`, doc 07 jog mapping (designed) |
| 2.8 | Master deck / master clock selection | 🟡 | Sync leader = "the other deck," automatic only. No explicit master-deck pick, no internal-clock master. | `TwoDeckBassEngine.ReapplyRate` (leader hard-coded), doc 11 (designed richer) |
| **Mixing** |
| 3.1 | Crossfader + curve | ✅ | Linear / Smooth (constant-power) / Sharp; pure + tested. | `MixerMath.CrossfaderGains` |
| 3.2 | Channel faders / gain | ✅ | Per-deck gain in mixer state. | `MixerMath.DeckOutputGain`, `MixerActionHandler` |
| 3.3 | 3-band EQ + kill | 🟡 | EQ shelves/peak built (RBJ biquads, ±24 dB); **"kill" not distinct** (full cut at knob 0 ≈ −24 dB, not true −∞ kill). | `MixerMath.EqBandCoefficients` |
| 3.4 | Filter (single-knob LP/HP sweep) | ✅ | Log-swept LP below center / HP above; tested. | `MixerMath.FilterCoefficients` |
| 3.5 | Gain / trim staging | 🔴 | Only one channel gain; no separate trim/auto-gain, no per-track normalization/ReplayGain. | — |
| 3.6 | Headphone cue (PFL) + cue mix | 🔴 | `MixerCueToggle` latches a flag only; **no cue bus, no ch 3/4 output, no cue-mix knob.** | doc 18 "SetCue still latches the flag only"; `BassMixer` |
| 3.7 | Master out | 🟡 | Master mix exists and feeds analysis; routed to **default stereo device** only. No master gain control, no master limiter. | `MasterAudioSource`, `BassMixerBackend.InitOutput` |
| 3.8 | Limiter / clip protection | 🔴 | No master limiter; per-deck DSP can sum > 1.0 with no soft-clip. | `BassMixerChannel`, `MixerMath` (no limiter) |
| **Audio I/O & quality** |
| 4.1 | Low-latency output | 🟡 | BASS default output + user buffer (10–200 ms clamp). **No ASIO/WASAPI-exclusive/WDM-KS path** despite doc 01 targeting <10 ms. | `BassInitOptions`, doc 01 targets (designed) |
| 4.2 | Multi-channel (master + cue) routing | 🔴 | Single stereo device. Multi-channel ASIO/CoreAudio out (master 1/2 + cue 3/4) deferred. | doc 18 deferred (repeatedly), doc 11 |
| 4.3 | Sample-rate handling / resampling | ✅ (analysis) / 🟡 (playback) | Analysis resamples to a fixed rate (`LinearResampler`); BASSmix master is fixed 48 kHz and BASS resamples deck streams. | `AudioFramePipeline`, `BassMixerBackend.CreateMaster` |
| 4.4 | Clipping protection on output | 🔴 | See 3.8. | — |
| 4.5 | CMD STUDIO 2A interface latency / device pick | 🟡 | Settings can pick a BASS output device + buffer; **no ASIO, no 4-ch cue, MIDI device not yet opened into the router.** | doc 18 Settings (deferred items) |
| **Time-stretch / pitch / key** |
| 5.1 | Keylock quality (time-stretch) | 🔴 | None (see 1.7). Would require BASS_FX or a stretch lib. | `BassMixerBackend.SetDeckRate` |
| 5.2 | Key shift | 🔴 | Not addressed. | — |
| 5.3 | Key detection | ✅ | Krumhansl–Schmuckler chroma template match, offline. | `ChromaExtractor.cs`, `KeyClassifier.cs` |
| 5.4 | Camelot / harmonic mixing hint | ✅ | Camelot codes + compatibility + harmonic set builder + MCP tools. | `Camelot.cs`, `HarmonicSetBuilder.cs`, `Liveolator.Mcp` |
| **Library & metadata** |
| 6.1 | Browse / search / sort | 🟡 | Catalog scanned + table with Artist column + detail panel; sort/search depth unverified. | `Liveolator.App/Features/Libraries`, doc 18 |
| 6.2 | Crates / playlists | 🟡 | Live Now/Next/Later queue model built (`LivePlaylist`); **no crates/folders, no saved setlists** (sessions "planned"). | `Playlist/LivePlaylist.cs`, doc 13 (sessions planned) |
| 6.3 | Tags / BPM / key columns | ✅ | ATL.NET tags + BPM + key + Camelot surfaced in Libraries detail. | `AtlMetadataReader.cs`, doc 18 |
| 6.4 | History / played log | 🟡 | `TrackState.Played` modeled but **not surfaced**. | `Playlist/TrackState.cs`, doc 18 |
| 6.5 | Import from rekordbox / Serato / iTunes | 🔴 | Not addressed at all (no parsers for `.crate`, rekordbox XML/DB, iTunes XML). | — |
| 6.6 | File-format support | ✅ | WAV (managed) + FFmpeg-CLI for compressed; BASS for realtime. | `CompositeAudioDecoder.cs`, `BassMixerBackend.OpenDeckStream` |
| 6.7 | Waveform overview | ✅ | Pure peak reducer + offline provider + strip control + playhead. | `Core/Waveform`, `DecodedWaveformProvider.cs` |
| 6.8 | Scrolling/zoomed playing waveform + beat markers | 🔴 | Only a static overview strip. No scrolling detail waveform, no beat-grid overlay (deferred), no minute markers. | doc 18 Waveform (deferred), DeckViewModel |
| **Performance aids** |
| 7.1 | Quantize (snap actions to grid) | 🟡 | Beat scheduler/quantizer primitives built for **visuals**; deck Quantize is a no-op (see 2.5). | `Beat/BeatQuantizer.cs` vs `TwoDeckBassEngine.SetQuantize` |
| 7.2 | Slip mode | 🔴 | Not addressed. | — |
| 7.3 | Censor / reverse | 🔴 | Not addressed. | — |
| 7.4 | FX (echo/reverb/delay/filter) | 🔴 | Only the mixer filter (3.4). No send/insert FX unit, no echo/reverb/delay. | — |
| 7.5 | Sampler | 🔴 | Not addressed (out of scope-ish per doc 00). | — |
| 7.6 | Recording the mix | 🟡 | Master tap exists (`MasterAudioSource`) so it's *feasible*; **no record-to-file** built; REC button static in UI. | `MasterAudioSource`, doc 18 (Program Out "REC static") |
| 7.7 | Streaming (input or broadcast) | ⚪ | Capture sources exist for VJ input; broadcast out is out of scope. | `Capture/`, product scope |
| 7.8 | Auto-mix / auto-transition | 🟡 | Designed in depth (intro/outro cues, tempo-match window); **autopilot rule engine built**, but the DJ-side auto-mix crossfade/transition driver is not. Silence cue detector exists. | doc 11 Auto-Mix, `Autopilot/`, `SilenceCueDetector.cs` |
| **Reliability** |
| 8.1 | Audio-dropout / xrun handling | 🟡 | RT-thread rules documented (doc 01); managed per-buffer DSP allocates (`new float[count]` in `ApplyChannelDsp`) — **allocation on the audio path**, against doc 01's own rule. | `BassMixerBackend.ApplyChannelDsp` (line 159) |
| 8.2 | End-of-track handling | 🔴 | No auto-advance/auto-cue/end-warning on the deck engine; mixer is `MixerNonStop`; deck just runs out. | `TwoDeckBassEngine` (no end event), doc 09 (auto-advance designed for single-deck only) |
| 8.3 | Missing-file handling | 🟡 | Load throws on bad stream (caught + logged + rethrown); waveform degrades to empty. Per-deck load error not surfaced to UI gracefully. | `TwoDeckBassEngine.Load` (line 148), `DecodedWaveformProvider` |
| 8.4 | Crash safety / session restore | 🟡 | Catalog + scan folders persist; **live session (loaded decks, cues, pitch) not persisted** (sessions "planned"). | doc 13 (sessions planned), doc 18 |
| **Controller integration** |
| 9.1 | CMD STUDIO 2A mapping | 🟡 | Mapping engine + learn mode + profiles fully built and pure; **no CMD STUDIO 2A profile captured, native MIDI (`Liveolator.Midi`) not opened into the router in the running app.** | `Core/Mapping/*` (built), doc 07 (profile uncaptured), doc 18 Settings (MIDI open deferred) |
| 9.2 | Jog-wheel seek/scratch mapping | 🔴 | Depends on 1.6. | doc 07 risks |
| 9.3 | LED/feedback to controller | ✅ (logic) / 🟡 (wired) | `MidiFeedbackPublisher` built; native output binding not wired in app. | `MidiFeedbackPublisher.cs`, doc 18 |

---

## 3. Critical gaps (must-have to be a "standard good DJ app")

Ordered by how much they block Liveolator from being a credible DJ tool *on its own terms*.

1. **Beatgrid with a first-beat / downbeat anchor (and phase sync on top of it).**
   - *Missing:* there is no `BeatGrid` type anywhere in `src/`. `TrackAnalyzer` returns only
     `BpmResult` (BPM + confidence). `TwoDeckBassEngine.SetQuantize` is an explicit no-op
     ("beat-grid quantize is not yet implemented"), and `TempoSyncCalculator` matches tempo
     but never phase.
   - *Why it matters:* tempo-matched-but-phase-unaligned decks sound clashy — to a DJ that is
     **not "synced."** Liveolator's entire product thesis (doc 00: "effortless one-button sync,"
     phase as a separate snap) and its beat-synced-visuals differentiator both rest on a
     trustworthy grid. This is the keystone.
   - *Where it lives:* add `BeatGrid { FirstBeatSeconds, Bpm, IsManual }` to
     `Liveolator.Core/Analysis` (+ extend `TrackAnalysisResult`/`BpmResult`); thread it to the
     engine via the existing `SetDeckBaseBpm` seam (extend to carry an anchor); implement phase
     correction in `TwoDeckBassEngine` using the ±5%-capped beat-distance model already specified
     in doc 11. Persist with `IsManual` protection (doc 13 §3).
   - *Effort:* **L** (analysis + engine + persistence + tests).

2. **Headphone cue / PFL bus + multi-channel output.**
   - *Missing:* `MixerCueToggle` only latches a flag; there is no cue bus, no ch 3/4 routing, no
     cue-mix. Output is one stereo device.
   - *Why it matters:* **you cannot DJ without pre-listening.** Beatmatching, cueing the next
     track, and dropping on time are impossible without a headphone feed. The CMD STUDIO 2A's
     whole reason for its 4-ch interface is this. This is table-stakes, not a nicety.
   - *Where it lives:* `Liveolator.Core/Mixer` (cue-bus model + PFL gain math) +
     `Liveolator.Audio/Playback` (a second BASSmix output stream on ch 3/4, or a second device);
     wire `MixerCueToggle` end-to-end. Requires the multi-channel/ASIO output path (doc 01 Phase 1b
     / doc 11) that is currently deferred.
   - *Effort:* **L** (needs the multi-channel output backend first).

3. **Loops (beat loops + manual in/out, at minimum).**
   - *Missing:* `DeckSetLoop` enum value exists but is unhandled; the engine has no loop concept.
   - *Why it matters:* loops are a core mixing/transition tool (extend an outro, build tension,
     cover a phrase). Their absence makes clean transitions and any "hold the energy" move
     impossible.
   - *Where it lives:* `TwoDeckBassEngine` (loop region via `BassMix.ChannelSetPosition` on a
     sync callback) driven by `DeckActionHandler` handling `DeckSetLoop`; beat-length → time
     conversion uses the per-deck base BPM already threaded via `SetDeckBaseBpm` (the doc-noted
     unblock) — but auto/beat loops also want the grid anchor from gap #1 to be musical.
   - *Effort:* **M**.

4. **Real cue points (settable temporary cue + persisted hot cues + LED).**
   - *Missing:* `Cue` only jumps to track start; hot cues are RAM-only, cleared on reload, not on
     the grid, and have no pad LED (the `ActionFeedbackChanged` model has no cue-index field).
   - *Why it matters:* setting a cue at the drop and triggering hot cues live is fundamental
     performance vocabulary. Losing cues on reload makes them useless for prepared sets.
   - *Where it lives:* settable cue in `TwoDeckBassEngine.Cue`; persist hot cues per track in the
     analysis/session store (doc 13 `LivePerformanceSession` / track cache); extend the feedback
     model with a cue-index for per-pad LEDs (`Core/Actions`).
   - *Effort:* **M**.

5. **Output quality: master limiter + no allocation on the audio thread.**
   - *Missing:* no master limiter / soft-clip; summed decks can exceed full-scale. And
     `BassMixerBackend.ApplyChannelDsp` / `OnMasterDsp` allocate a `new float[]` **every buffer on
     the BASS update thread**, violating doc 01's own non-negotiable "no allocation on the audio
     callback thread" rule — a GC pause here is an audible glitch.
   - *Why it matters:* clipping on the master is the fastest way to sound amateur (and to anger a
     sound engineer); audio-thread GC causes dropouts under load. Both are correctness/reliability
     issues, prioritized above features per the user's own standards.
   - *Where it lives:* a pure soft-clip/limiter in `Core/Mixer/MixerMath` applied on the master tap;
     reuse pre-allocated scratch buffers in `BassMixerBackend` instead of per-call `new`.
   - *Effort:* **S–M**.

---

## 4. Important gaps (expected by pros, not strictly blocking)

- **Keylock / master tempo (5.1).** Pitch is vinyl-style only. Pros expect ±6–8% tempo moves
  *without* chipmunk pitch, and key-locked harmonic mixing. Needs BASS_FX (`ManagedBass.Fx`,
  currently deliberately avoided) or a stretch implementation. Effort **M–L**.
- **Scrolling/zoomed playing waveform + beat-grid overlay (6.8).** The static overview is fine for
  load-time; live mixing wants a scrolling detail waveform with beat markers to mix by eye.
  Effort **M**.
- **End-of-track handling + auto-advance per deck (8.2).** Warning, auto-cue-to-next, or stop;
  today a deck silently runs out. Effort **S–M**.
- **Live session persistence (8.4).** Restore loaded decks, cues, pitch, queue after a crash/restart
  — designed (`LivePerformanceSession`) but unbuilt. Effort **M**.
- **Explicit master-deck / internal-master clock selection (2.8).** Leader is hard-coded as "the
  other deck." Pros expect to pin a master and to run to an internal clock. Effort **S**.
- **Per-deck temporary pitch-bend / nudge on audio (2.7).** Beat-clock nudge exists; the deck audio
  has no transient pitch-bend for manual phase pushes. Effort **S**.
- **Auto-mix transition driver (7.8).** The cue detection + autopilot engine exist; the actual
  crossfade-on-cue DJ transition is unbuilt. This is a stated *differentiator*, so arguably it
  belongs higher once the grid/cue foundations land. Effort **M**.
- **CMD STUDIO 2A profile capture + MIDI wired into the running app (9.1).** All the mapping logic
  is built and tested, but no hardware profile is captured and `Liveolator.Midi` is not opened into
  `MidiControllerRouter` at composition. Until then the app is mouse-only. Effort **S** (capture) +
  **S** (wiring).
- **EQ "kill" (3.3).** True low/mid/high kill (−∞) is a staple of EQ-out transitions; −24 dB max
  is audible bleed. Effort **S**.
- **ReplayGain / per-track auto-gain (3.5).** Avoids level jumps between tracks. Effort **S–M**.

---

## 5. Nice-to-have / differentiators

- **Beat-synced visuals via the shared clock** — already the project's strategic bet and partly
  built (`BeatTimeline`, `QuantizedLaunch`, visual handler). Once the audio grid (gap #1) is real,
  this becomes the actual differentiator vs Serato/rekordbox. Lean into it.
- **Send FX unit (echo/delay/reverb) with beat-synced time** — quantized FX off the shared clock
  would be a natural, on-brand feature. Out of scope today (doc 00 says no "pro-FX maximalism") but
  a single beat-synced echo is cheap and high-impact.
- **Harmonic auto-suggest in the live queue** — Camelot logic + `HarmonicSetBuilder` already exist;
  surfacing "compatible next track" hints in the deck-load UI is low-cost.
- **rekordbox/Serato library import (6.5)** — large but a real adoption lever for working DJs with
  existing libraries; could reuse cue/grid data instead of re-analyzing.
- **MCP agent-driven mixing** — the `Liveolator.Mcp` surface is unusual and could let an AI agent
  build/skip/transition sets; a genuine differentiator if extended to live transport.

---

## 6. Risks & unknowns

- **BASS/ManagedBass audio path is unverified on real hardware.** Every BASS-touching type
  (`BassMixerBackend`, `BassPlayback`, capture) is explicitly "verified manually, not in CI." There
  is no proof that two decks + per-deck managed DSP + master tap actually hold a real-time deadline
  on the CMD STUDIO 2A. **The per-buffer allocation in `ApplyChannelDsp` is a concrete latent
  glitch risk** (gap #5).
- **Latency target vs reality.** Doc 01 aims <10 ms; the built path uses BASS's default output with
  a 10–200 ms user buffer and **no ASIO/exclusive/WDM-KS**. The low-latency story is currently
  aspirational. CMD STUDIO 2A cue output (ch 3/4) is entirely unbuilt, so the headline hardware fit
  is unproven.
- **Keylock/time-stretch quality is unknown because it does not exist.** The decision to avoid
  BASS_FX keeps things simple but means any future keylock is greenfield and its quality (artifacts
  at ±6–8%) is unvalidated.
- **Beatgrid correctness is the biggest algorithmic unknown.** The BPM detector is a single global
  autocorrelation over the whole onset envelope (`TempoEstimator`) — it yields *a* BPM but **no
  phase, no downbeat, no per-section grid**, and no octave disambiguation surfaced (it just returns
  the strongest lag in 70–180 BPM). For variable-tempo or live tracks this will drift. Doc 03's
  richer `OnsetDetectionEngine`/`BeatTracker`/`BeatGrid` design is **unbuilt**. Confidence is a
  relative autocorrelation peak, not a calibrated reliability.
- **Waveform rendering is overview-only.** No scrolling/zoom render exists; the cost/perf of a
  60 fps scrolling waveform in Avalonia is untested.
- **MIDI on real hardware unproven.** `Liveolator.Midi` (RtMidi) exists as a Settings dependency but
  is not driving the dispatcher; the CMD STUDIO 2A MIDI map is not captured. Relative-encoder
  encodings and jog behavior are unknowns until learned on the device.

---

## 7. Recommended next 3–5 build steps (priority order)

1. **Build a real `BeatGrid` (first-beat anchor) in offline analysis, then implement phase sync.**
   Extend `Liveolator.Core/Analysis` (`BeatGrid`, add anchor to `TrackAnalysisResult`), thread the
   anchor to the engine (extend the `SetDeckBaseBpm` seam or add a grid seam), and turn
   `TwoDeckBassEngine.SetQuantize` from a no-op into the ±5%-capped beat-distance phase correction
   doc 11 already specifies. Add `IsManual` grid protection (doc 13). *This unlocks gaps #1, #3
   beat-loops, #4 on-grid cues, auto-mix, and beat-synced visuals.* (doc 03, doc 11, doc 16)

2. **Stand up the multi-channel output + headphone-cue (PFL) bus.** Implement the deferred doc 01
   Phase 1b / doc 11 output path (ASIO/CoreAudio or a second device), add a cue-bus model + PFL math
   to `Core/Mixer`, and wire `MixerCueToggle` to route ch 3/4 on the CMD STUDIO 2A. Verify latency on
   real hardware. *Table-stakes for actually DJing.* (doc 01, doc 11)

3. **Fix the audio-thread hot path and add a master limiter.** Remove per-buffer allocation in
   `BassMixerBackend` (pre-allocated scratch), add a pure soft-clip/limiter on the master in
   `MixerMath`. Small, high-value reliability/quality win that also de-risks step 2's hardware test.
   (doc 01 RT rules, doc 11)

4. **Implement loops and real cue points.** Handle `DeckSetLoop` in `DeckActionHandler` + a loop
   region in `TwoDeckBassEngine` (beat-length via base BPM); make `Cue` a settable temporary cue;
   persist hot cues per track and add a cue-index to the feedback model for pad LEDs. (doc 11,
   doc 13)

5. **Wire the controller end-to-end + capture the CMD STUDIO 2A profile.** Open `Liveolator.Midi`
   into `MidiControllerRouter` at composition, run learn mode against the CMD STUDIO 2A to capture
   its map (jogs, EQ, faders, hot-cue pads, cue), and ship the default profile. *Turns the app from
   mouse-only into a real controller workflow.* (doc 05, doc 07, doc 18 Settings)

> After steps 1–4, Liveolator is a genuinely usable minimal DJ app (sync that actually aligns,
> headphone cue, loops, cues, clean master). Steps toward keylock, scrolling waveforms, auto-mix,
> and library import are then the path from "usable" to "competitive within its niche."
