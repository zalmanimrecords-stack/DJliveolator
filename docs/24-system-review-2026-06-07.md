# 24 — System Review, Bug Map & Forward Plan (multi-expert)

> **Purpose:** the full-system review the owner asked for, conducted as a panel of ten domain
> experts (senior DSP engineer, tempo/sync specialist, professional touring DJ, VJ/real-time
> graphics engineer, hardware-controller engineer, music-library/metadata engineer, DJ-software
> UX designer, principal software architect, QA/release engineer, DJ-gear product manager). Each
> expert read the **actual current code** (including the uncommitted in-flight sync + session work)
> and every High/Critical bug was **adversarially re-verified** against the source before being
> recorded here. Date: **2026-06-07**.
>
> **Baseline measured for this review:** solution **builds clean (exit 0)** and the full suite is
> **1,279 tests passing, 0 failed, 0 skipped** across 8 projects (Core 650, App 234, Audio 162,
> Media 92, Visuals 66, Integration 25, MIDI 27, Online 23) — up from the 1,051 recorded in
> `docs/22`, confirming the in-flight wave (continuous phase-lock sync, deck-driven shared clock,
> live-set persistence) landed **with** committed tests.
>
> Where this doc and `docs/18`/`docs/22` disagree on a code fact, **this doc wins** — it was
> measured against the working tree on 2026-06-07. `docs/18` remains the living module map.

---

## 0. Headline verdict

The architecture is genuinely strong and the discipline is real: one `PerformanceAction` dispatcher
seam, pure-and-tested Core with native/UI pushed to bindings, tolerant persistence, and TDD-first on
the newest code. The DSP and sync **math** is correct and well-covered. The project is much closer to
a credible DJ app than most greenfield efforts at this stage.

The gaps are concentrated and addressable, and they cluster into three themes:

1. **"Wired but not reachable / not effective."** Several headline features exist in Core, pass their
   unit tests, and are even surfaced in the UI — but a missing wire makes them do nothing in practice:
   the GL render loop never re-reads the scene, auto-advanced tracks load with no BPM, the EQ "kill"
   doesn't kill, the headphone-cue level/blend has no on-screen control, the jog wheel only nudges
   forward. These are the highest-ROI fixes: small changes that turn theatre into function.
2. **"The shared clock — the product's whole differentiator — has correctness and robustness holes."**
   The continuous beat the visuals lock to is mis-scaled when the master deck is pitched, the
   correction loop is pumped from the UI thread (starves during exactly the busy moments of a set),
   and the source-switch has a cross-thread data race.
3. **"No automated backstop."** There is zero CI, the two native-fetch scripts have already diverged
   (Mac/Linux silently lose FLAC), and packaging/distribution is entirely manual — risky for a project
   whose hard requirement is cross-platform distribution and which is edited by many parallel agents.

---

## 1. System map (by subsystem)

Status legend: **solid** (built + tested + effective) · **partial** (works but with a named limit) ·
**stub** (logged no-op / placeholder) · **orphaned** (built but not wired) · **risky** (a defect or
robustness hole below).

| Subsystem | Verdict | Highlights | Worst issue |
|-----------|---------|-----------|-------------|
| **DSP / audio engine** | solid | RBJ cookbook EQ/filter correct & a0-normalized; stereo-linked feed-forward limiter; equal-power cue math; RT path now allocation-free (grow-only scratch buffers) | Symmetric (not periodic) Hann in STFT; biquad history not reset on seek (filter transient) |
| **Beat / sync / shared clock** | partial | Octave-fold beatmatch, wrapped phase-error, proportional phase-lock loop, deck-driven shared clock + source multiplexer — all pure & tested | **Continuous beat mis-scaled by master pitch (HIGH)**; SwitchingBeatClock cross-thread race (MED) |
| **Decks / mixer / transport** | partial | Slot-addressed engine, CDJ back-to-cue, beat-loops, persistent hot cues, PFL pre-fade send | **EQ kill = −24 dB only (HIGH)**; **sync pumped from UI thread (HIGH)**; **auto-advance loses BPM (HIGH)**; hot-cue doesn't start playback (MED) |
| **Visuals / VJ** | stub→partial | Pure scene/layer/macro model + multi-layer premultiplied blend + beat flash off shared clock, all tested off-GPU | **Render loop never re-reads scene (HIGH)**; **no viewport/resize → broken on macOS Retina (HIGH)**; **effect chains never execute (HIGH)**; no video/camera; no authoring UI |
| **MIDI / controller** | partial | Clean library-agnostic mapping, correct 14-bit/relative decode, graceful degradation, LED feedback | **Jog wheel can't nudge backward / ignores magnitude (HIGH)**; no soft-takeover; no Push 1 profile; flat-binding model is one generation behind the Track-D plan |
| **Library / analysis / MCP** | solid | Failure-isolated incremental scan, tolerant versioned persistence, resumable background re-analysis, correct Camelot harmonic logic | **JsonCatalogStore save not serialized → torn catalog race (HIGH)**; SqliteCatalogStore orphaned/dead; no analyzer-version cache key; no manual-edit lock |
| **UI / UX (Avalonia)** | partial | Loop-safe feedback binding, honest disabled controls, advancing-playhead waveform, custom Knob/Fader | **`hardware-well` style undefined → mixer wells unstyled (MED)**; **Cue Level/Mix have no UI (HIGH)**; zero accessibility metadata on custom controls; some hardcoded hexes |
| **Architecture** | solid | Exemplary dispatcher (fail-fast on dup ownership, fault-isolated), Core purity intact, clean live-set seam | 8 orphan action kinds silently dropped (MED); sync heartbeat owned by one view-model (MED); payload can't express `VisualSourceRef`; `ServiceConfig.Build()` becoming a god-method |
| **Testing / build / CI** | partial | Excellent headless-clean unit discipline; in-flight code landed with tests; BASS behind fakes | **No CI at all (HIGH)**; **fetch-bass.sh omits BASSFLAC (HIGH)**; CopyBassNative doesn't verify required libs; UiShots are existence-only; docs/14 describes the dead projectM/NAudio stack |
| **Product / strategy** | — | Differentiator (one shared audio↔visual clock) is real and wired; DJ core A1–A5 done; library production-grade | VJ is unauthorable without hand-editing JSON; packaging/licensing unstarted; sync correctness hole (above) undercuts the differentiator |

---

## 2. Bugs (verified against code)

### 2.1 Critical / High — fix before any "it works" claim

| # | Bug | File | Why it matters | Fix |
|---|-----|------|----------------|-----|
| **B1** | **Shared clock's continuous beat is scaled by the master deck's pitch rate** (error factor = rate; zero only at ±0%). | `TwoDeckBassEngine.cs:447` | The headline audio↔visual lock visibly slips against the master deck whenever its pitch fader is off-centre — the differentiator breaks exactly when DJs beatmatch by ear. | Compute the continuous beat from **base** BPM against original-track position: `(posSeconds - firstBeat) / (60.0 / _baseBpm[master])`; keep `EffectiveBpm` only for the published audible tempo. Add a pitched-master sync test. |
| **B2** | **EQ "kill" only attenuates −24 dB** — not a kill. | `MixerMath.cs:77` (`MaxEqGainDb=24`) | The single most-used live move (bass swap) leaves the killed track's kick plainly audible (−24 dB ≈ 6% amplitude). Pro mixers kill to −∞/−60..−90 dB. | At the bottom of band travel, route to a true mute floor (≤ −48..−90 dB) or switch low/high bands to an actual cut. Add a test asserting full-cut ≥ ~48 dB at band centre. |
| **B3** | **Phase-lock correction loop is pumped only from the Avalonia UI `DispatcherTimer`.** | `LiveViewModel.cs:118` → `MasterClockBridge.Tick` → `TwoDeckBassEngine.UpdateSync:405` | The only thing holding a synced slave beat-locked stops ticking during window drag/resize, heavy repaint, GC, or a modal — the busy moments of a set — so the slave drifts until the UI frees up. | Pump `UpdateSync` from a high-priority non-UI cadence (dedicated timer/thread, or the audio binding's update callback). The `ISyncCorrectionDriver` seam already isolates this. |
| **B4** | **Live-queue auto-advance loads tracks with no base BPM / first-beat.** | `PlaylistAudioPlayer.cs:96` (bypasses the dispatcher; never calls `SetDeckBaseBpm`/`SetDeckFirstBeat`) | Every auto-advanced track loads with BPM=0 → `SetLoop` rejected, Sync stays uncorrected (no valid leader), shared visual clock won't follow. SYNC/LOOP silently do nothing. | Route auto-advance load through the `DeckLoadTrack` + `DeckSetFirstBeat` action path (preferred — one code path), or give the player a library reference to set base BPM/first-beat after load. Add a test. |
| **B5** | **GL render loop never reflects scene / bank / layer state changes.** | `GlVisualPerformanceEngine.cs:194` (`LoadRenderableLayers()` called once before the window) | Pressing a scene pad, switching banks, toggling a layer, or changing opacity changes **nothing on screen** — only brightness/flash/blackout uniforms are live, contradicting the docstrings. | Rebuild the renderer's textures from a dirty flag set by `SelectBank`/`LoadScene`/`ToggleLayer`/`SetLayerOpacity`, on the GL thread at frame start. |
| **B6** | **No GL viewport set and no `FramebufferResize` handling.** | `GlVisualPerformanceEngine.cs:213` (no `gl.Viewport`/resize anywhere in `Liveolator.Visuals`) | Resize leaves a stale viewport; on **macOS Retina** the framebuffer is 2× the logical size, so the quad covers a quarter of the surface. macOS is a hard product requirement. | On `window.Load` set `gl.Viewport` to **framebuffer** size; subscribe `FramebufferResize`. Use `FramebufferSize`, not logical `Size`. |
| **B7** | **Per-layer effect chains (`EffectRef`) are never executed.** | `SceneComposition.cs:29` drops `.Effects`; `LayeredQuadRenderer.cs:88` runs one fixed passthrough shader | The entire GLSL effect system (registry/descriptor/probe + 7 of 8 macros) is dead from the renderer — no echo/kaleido/particles/hue can ever render. This is the "Resolume-class" reason-to-exist. | Carry `EffectRef` into `ResolvedLayer`, compile descriptor shaders via the registry, run a per-layer FBO ping-pong effect pass, bind macros via `MacroTarget`. (Large.) |
| **B8** | **Jog wheel (relative encoder) can't nudge backward and ignores turn magnitude.** | `CmdStudio2AProfile.cs:108` (bound to `BeatNudgeForward`) + `BeatActionHandler.cs:70` never reads `action.Value` | On the primary DJ control, both turn directions nudge forward by a fixed step — beatmatching by ear via the jog is impossible. | Make `BeatActionHandler` honor the relative delta (`nudge = action.Value * step`, sign carries direction), mirroring `MixerActionHandler`'s relative crossfade. Add a negative-step test. |
| **B9** | **`JsonCatalogStore.SaveAsync` is not serialized** — background re-analysis and a foreground scan race on a fixed `.tmp` path. | `JsonCatalogStore.cs:200` (singleton at `ServiceConfig.cs:152`) | Concurrent saves (startup scan while background re-analysis runs) throw a sharing violation or clobber mid-write → torn/partial `catalog.music.json`. `JsonHotCueStore` already guards this with a `SemaphoreSlim`. | Add a `SemaphoreSlim` around `SaveAsync` and use a unique temp name (`path+"."+Guid+".tmp"`), mirroring `JsonHotCueStore`. |
| **B10** | **`fetch-bass.sh` omits BASSFLAC** (the `.ps1` fetches it). | `scripts/fetch-bass.sh:113` | Mac/Linux contributors following the documented setup get no `libbassflac` → FLAC tracks neither play nor draw a waveform. This is the exact cross-platform parity the project exists to guarantee. | Add a non-fatal `fetch_lib bassflac` and update the header; better, unify both scripts behind one shared `(base, required)` manifest so they can't diverge again. |
| **B11** | **No CI pipeline exists.** | `.github/` absent; no CI config anywhere | TDD and "validate before finishing" have no automated backstop; with many parallel agents on the shared tree, a regression/break is caught only if a human runs the suite. Root cause of the "verified manually, not in CI" worry. | Add a GitHub Actions matrix (windows-latest + macos-latest) running `dotnet build` + `dotnet test` on push/PR; gate merges. The suite is already headless-clean. |
| **B12** | **Headphone Cue Level + Cue/Master blend have no UI binding** (VM-complete, view-absent). | `MixerViewModel.cs:50` (no `.axaml` binds `CueLevel`/`CueMix`) | The headphone-monitoring level and cue/master blend — central to the CMD STUDIO 2A cue interface the product sells on — cannot be set from screen at all. | Add a Knob (CueMix) + small Fader/Knob (CueLevel) to `MixerView.axaml` in the cue row. (Small.) |

### 2.2 Medium

- **SwitchingBeatClock cross-thread data race** (`SwitchingBeatClock.cs:34`) — `Select()` swaps `_active` and re-wires `StateChanged` with no lock while the audio-driven base clock raises `StateChanged` on the BASS thread. Can drop/double-deliver a beat event at the moment of a master switch (occasional visual-beat glitch). Guard `_active`/subscribe/unsubscribe with a lock or marshal `Select` to the raising thread.
- **Eight declared action kinds have no owning handler** (`PerformanceActionKind.cs:13` — `Transport*` except Stop, `AutoMix*`) — dispatched intent is silently logged-and-dropped. Implement a `TransportActionHandler`/`AutoMix` handler, or remove the kinds; add a startup invariant test that every kind is owned by exactly one handler.
- **Sync heartbeat owned by one view-model** (`LiveViewModel.cs:118`) — the correction loop + shared-clock pump only run while the `LiveViewModel` singleton's timer is alive (architectural twin of B3). Move to a composition-root-owned `PerformanceClockLoop` service.
- **Scene-load LED/UI feedback lights despite a no-op engine load** (`VisualActionHandler.cs:140`) — the Scene Grid / Push pad shows a scene as active while the compositor still shows the startup scene. Gate "active" feedback on real engine confirmation (resolved once B5 lands).
- **`SqliteCatalogStore` is orphaned dead code** (`ServiceConfig.cs:152` still uses `JsonCatalogStore`; SQLite store referenced only by its own tests) — the "single DB gateway" commit shipped an unused gateway. Wire it (with a JSON→SQLite migration) or remove it; note it opens with `Pooling=false`, no `busy_timeout`/WAL.
- **Analysis cache has no analyzer-version key** (`ScannedFile.cs:7` fingerprint = size+mtime only) — improving BPM/key algorithms will never re-analyze already-OK tracks. Record an analyzer version and treat a bump like a Modified file.
- **Hot-cue trigger never starts playback** (`TwoDeckBassEngine.cs:664`) — pads only seek while paused; every CDJ/Serato/rekordbox plays on hot-cue. Start playback on jump (and model momentary play-while-held once the action seam carries press/release).
- **MCP `ListTracks` re-implements filter/sort** (`LibraryTools.cs:30`) instead of reusing Core `TrackQuery.Apply` — drift from the thin-adapter rule; extend `TrackQuery` to take the sort key and call it from both UI and tool.
- **Only the `brightness` macro is honored** (`GlVisualPerformanceEngine.cs:135`) + **scene `BeatBehavior`/`MacroValues` never applied** (`VisualScene.cs:29`) — 7 of 8 Push/UI encoders are inert and per-scene look/reactivity is ignored (resolved with B7).
- **No accessibility metadata on custom controls** (`Knob.cs:56`) — Knob/Fader/WaveformStrip have no `AutomationProperties`/automation peer and no focus-visible ring; navy-on-navy + no focus indicator fails the docs/19 keyboard/contrast intent.

### 2.3 Low

- **Symmetric Hann where periodic is intended** (`Window.cs:14`, `/(size-1)` vs `/size`) — feeds onset-flux + chroma STFT; marginal spectral bias. Use the periodic form; fix the docstring.
- **Biquad delay history not reset on seek/cue/loop-wrap** (`BassMixerBackend.cs:365`) — brief filter transient when an EQ band/filter is engaged. Add `StatefulBiquad.Reset()` and call it on position discontinuities.
- **`FirstBeatEstimator` integer beat-period folding drifts phase** on long tracks (`FirstBeatEstimator.cs:30`) — skews the initial downbeat anchor by tens of ms. Fold by the fractional period.
- **Re-snap seek uses a latency-compensated position** (`TwoDeckBassEngine.cs:515`) — harmless at the default 0 latency, but mis-seeks once a real output latency is configured. Re-snap against the true playhead; keep latency only in the phase-error measurement.
- **Hardcoded hexes** on the deck-id badge + MASTER label (`DeckView.axaml:16`) violate the token rule.
- **`docs/18` self-contradicts** on the compositor (line 49 "one-layer" vs line 468 "multi-layer") — stale block to remove.

---

## 3. Recommendations (cross-cutting, beyond the bug fixes)

1. **Move the realtime correction/clock pump into an engine-lifetime service** (closes B3 + the medium "heartbeat owned by a view-model"), so sync and the shared visual clock run independently of which tab is open — a prerequisite for headless/autopilot playback.
2. **Add a composition-time invariant test** asserting every `PerformanceActionKind` is owned by exactly one registered handler (the dispatcher already rejects double-ownership; this closes the zero-ownership gap that produced the 8 orphan kinds).
3. **Register `ISyncCorrectionDriver` in DI** rather than handing `MasterClockBridge` the concrete `TwoDeckBassEngine` — the clean seam already exists for exactly this.
4. **Split `ServiceConfig.Build()`** (~340 lines) into focused `Add*` module registrations — keeps the single root while restoring the project's own small-focused-file standard.
5. **Pool the last audio-thread allocation** (the master-tap hand-off buffer at `BassMixerBackend.cs:580`) to make the RT path fully allocation-free per the doc-01 invariant.
6. **Make EQ-kill and pitch range first-class configurable behaviors** (pitch is baked at ±8%; pros expect selectable 6/8/10/16/wide; kill needs a true mute floor).
7. **Stream the analysis decode** instead of buffering the whole track twice (`TrackAnalyzer.cs:62`) — ~13M floats copied 2–3× per track hurts large scans.
8. **Add soft-takeover to absolute knobs/faders** even in the flat-binding model — a contained step toward Track-D D3 that prevents value jumps on profile/context change.
9. **Subscribe `MacroEncoders` to `VisualSetMacro` feedback** so on-screen knobs track a physical Push encoder (currently one-way).
10. **Strengthen UiShots from existence-only to a baseline image comparison**, and **rewrite `docs/14`** (still the dead projectM/NAudio/Spotify-loopback stack) to the Avalonia/BASS/fake-backend reality.

---

## 4. Missing features (by track)

**DJ (Track A):** keylock / master-tempo (tempo without pitch — needs `ManagedBass.Fx`); manual loop in/out + reloop/exit; loop-length / halve-double / beat-jump UI; cue-play preview (press-and-hold); explicit master-deck pinning + global master BPM; per-deck VU/clip metering; track time-remaining readout; tempo octave-error resolution in BPM detection.

**Library (Track B):** manual-edit lock on analysis fields; canonical-key storage with derived notations (Camelot/Open-Key from one pitch-class+mode); JSON→SQLite migration path; MCP tools for sample/visual-aware queries and re-analysis control; reconcile online-enrichment results back into the catalog.

**VJ (Track C):** the VJ authoring UI (scene/layer/effect editor — the single biggest VJ gap); video-clip + live-camera rendering (FFmpeg→GL texture, capture); GLSL effect-execution pipeline (B7); quantized scene/clip launch on the shared grid; transitions (crossfade/wipe); `VisualSetLayerSource` payload; multiple output windows / external-display targeting.

**Controller (Track D):** Push 1 control-surface profile; device/mode-aware, context-following Control Surface abstraction + selection service; soft-takeover; Push 1 SysEx LED/LCD formatting; headphone-cue/hot-cue/loop/pitch bindings in the DJ profile; manual override layer.

**Sync / clock:** Ableton Link / external clock source; beat-phase re-anchor on master change; manual nudge of a synced deck.

**Platform / release:** cross-platform CI matrix; scripted per-RID packaging job with native BASS staged; a code-coverage gate.

---

## 5. The 10 next steps (recommended order)

These are sequenced for **maximum credibility per unit of effort**: the quick "make it real" fixes
first (they convert already-built work into working features), then the differentiator-correctness
and safety-net work, then the larger build-outs.

1. **Land a CI pipeline (B11).** GitHub Actions, windows+macos matrix, `build` + `test` on push/PR, gate merges. *S — do this first; it protects everything after it.*
2. **Fix `fetch-bass.sh` BASSFLAC + unify the two fetch scripts (B10).** Restores Mac/Linux FLAC parity. *S.*
3. **Fix the shared-clock pitch-scaling bug (B1) + move the sync pump off the UI thread (B3).** The differentiator must be correct and robust before anything is built on top of it. *M.*
4. **Route auto-advance through the action layer so it carries BPM/first-beat (B4).** One code path; unblocks Sync/loops/visual-follow on every queued track. *S.*
5. **Make the EQ kill a true kill (B2) + add the on-screen Cue Level/Mix controls (B12) + fix the `hardware-well` style.** The three fixes that make the mixer behave like a real DJ mixer. *S–M.*
6. **Make the GL render loop re-read the scene on a dirty flag (B5) + add viewport/resize handling (B6).** Turns the entire visual action vocabulary from theatre into output and fixes the macOS-Retina blocker. *S–M.*
7. **Honor the relative jog delta (B8) + serialize `JsonCatalogStore` saves (B9).** Two small, isolated, high-impact correctness fixes (jog beatmatch; catalog integrity). *S.*
8. **Add the dispatcher-completeness invariant test + decide the 8 orphan action kinds (implement `TransportActionHandler` or remove).** Closes the silent-drop class the dispatcher was designed to prevent. *S–M.*
9. **Resolve the catalog persistence story: wire `SqliteCatalogStore` (with a JSON→SQLite migration) or delete it, and add an analyzer-version cache key + manual-edit lock.** Removes dead code and makes algorithm upgrades + manual fixes safe. *M.*
10. **Build the VJ effect-execution pipeline (B7) as the first slice of the VJ authoring track (Track C).** The largest remaining differentiator gap; per-layer FBO effect pass + macro→uniform binding activates the already-built effect registry and the 7 inert encoders. *L.*

> Steps 1–2 are the safety net, 3–7 are "make the built features actually work" (highest ROI),
> 8–9 are structural hygiene, and 10 opens the next major build-out. Steps 1, 2, 4, 7 can run in
> parallel with 3 and 5/6 on separate branches/worktrees per the parallel-agent workflow — but
> finish each through the test gate before merge.
