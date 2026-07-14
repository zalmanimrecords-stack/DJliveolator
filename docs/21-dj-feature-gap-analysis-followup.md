# 21 — DJ Feature Gap Analysis (Follow-up after the integration merge)

> **Author's lens:** same working club/festival DJ + DSP engineer who wrote
> [`docs/20`](20-dj-feature-gap-analysis.md), now re-auditing the **integrated** tree
> (`feat/final-integration`, worktree `.claude/worktrees/integration`) after a coordinated
> multi-agent merge of the gap-closing branches. Method: I did **not** trust the merge
> commit messages — I traced every claim from `PerformanceAction → handler → engine/backend`
> in real code, and treated "a calculator class exists" as *not* the same as "reachable and
> wired." Date: 2026-06-06. Build + full test suite were run (see §6).

---

## 1. Executive summary

This merge is a **large, real step forward** — the biggest single jump the DJ side has taken.
Four of the five Critical gaps moved from "missing/no-op" to **working code that is reachable
through the action layer and the live UI**: the master limiter + RT-thread allocation fix are
genuinely done; loops are fully implemented end-to-end (action → handler → `TwoDeckBassEngine`
→ BASS sync callback); phase-sync/Quantize is now a real beat-distance alignment instead of a
logged no-op; and hot cues are set/jumped through the dispatcher with feedback. The headphone-cue
(PFL) **bus, controls, second output device, and cue-mix math** are all built and wired. The tree
is green: **1051 tests pass, 0 fail**, and the solution builds with 0 warnings.

**Headline change:** Liveolator went from "a two-deck tempo-matched blender" to a deck engine that
can *loop, phase-align, hold cues, monitor on a second output, and protect the master* — and almost
all of it is reachable from the live UI, not just from unit tests.

**Single most important remaining gap:** the **beatgrid first-beat anchor never reaches the engine**,
so phase-sync runs blind. The full chain exists — `FirstBeatEstimator` runs in analysis,
`BpmResult.FirstBeatSeconds` is computed and stored on `MusicTrack.Bpm`, the engine has
`SetDeckFirstBeat` and a correct `PhaseAlignmentCalculator` — **but nothing ever calls
`SetDeckFirstBeat`.** The `DeckLoadTrack` action carries only a single `Value` (the BPM), so the
anchor is dropped at the UI→action boundary and both decks phase-align against an anchor of `0`.
This makes gap #1 *the* keystone again: Quantize is wired but musically meaningless until the anchor
is threaded through. A close second is that **per-deck PFL pre-listen is still not audible** — the
cue bus carries only the master leg, so you can blend cue/master but cannot actually pre-listen to a
cued, non-playing deck (the DJ's core reason for headphones).

---

## 2. Updated coverage matrix (BEFORE → AFTER)

Status key: ✅ Built & wired (reachable through action/UI) · 🟡 Built but not fully wired (logic
exists; a seam/DI/anchor is missing) · 🔴 Missing · ⚪ N/A / out-of-scope.

| # | DJ feature | Before | After | Note + real reference |
|---|-----------|--------|-------|------------------------|
| **Decks & transport** |
| 1.1 | Play / pause / stop | ✅ | ✅ | Unchanged, per-slot through dispatcher. `TwoDeckBassEngine.PlayPause/Stop`, `DeckActionHandler` |
| 1.2 | Temporary (back-to-cue) cue | 🟡 | 🟡 | Still jumps to **track start** + pauses; not a settable cue. `TwoDeckBassEngine.Cue` (line 258) |
| 1.3 | Hot cues | 🟡 | 🟡 | Set/jump now reachable via `DeckHotCue` action + feedback; **still RAM-only** — the persistent store exists but is orphaned (see §4). `TwoDeckBassEngine.HotCue` (449), `DeckActionHandler.TriggerHotCue` |
| 1.4 | Loops (beat loops) | 🔴 | ✅ | **Closed.** `DeckSetLoop` handled → `SetLoop`/`ClearLoop` → BASS loop sync. `DeckActionHandler.SetLoop`, `TwoDeckBassEngine.SetLoop` (477), `BassMixerBackend.SetDeckLoop` (323), `BeatLoopCalculator` |
| 1.5 | Seek | ✅/🟡 | ✅/🟡 | Engine seek + UI click-to-seek emit `DeckSeek` (DeckViewModel line 96). |
| 1.6 | Vinyl / scratch / jog | 🔴 | 🔴 | Still deferred. |
| 1.7 | Keylock / master tempo | 🔴 | 🔴 | Still vinyl-style frequency scaling; no BASS_FX. `BassMixerBackend.SetDeckRate` (294) |
| **Tempo & sync** |
| 2.1 | Offline BPM detection | ✅ | ✅ | Unchanged. `TempoEstimator`, `BpmDetector` |
| 2.2 | Beatgrid (first-beat/downbeat anchor) | 🔴 | 🟡 | **Big move but not closed.** `FirstBeatEstimator` built + wired into `BpmDetector`; `BpmResult.FirstBeatSeconds` carried on `MusicTrack.Bpm`. **But the anchor is never threaded to the engine** (see §3/§4). `FirstBeatEstimator.cs`, `BpmResult` (BpmDetector line 12) |
| 2.3 | Manual beatgrid edit | 🔴 | 🔴 | Still none. |
| 2.4 | Beat sync (tempo match, ½×/2×) | ✅ | ✅ | Unchanged + still used on load/pitch/leader change. `TempoSyncCalculator`, `TwoDeckBassEngine.ReapplyRate` |
| 2.5 | Phase sync / Quantize | 🟡 | 🟡 | **Real now, but blind.** `SetQuantize` calls `PhaseAlignToLeader` using `PhaseAlignmentCalculator` — no longer a no-op. But `_firstBeat` is always 0 (never set), so it aligns to a wrong/absent grid. `TwoDeckBassEngine.PhaseAlignToLeader` (383) |
| 2.6 | Tempo range / pitch fader | ✅ | ✅ | Unchanged (±8%). |
| 2.7 | Per-deck pitch-bend / nudge | 🟡 | 🟡 | Still beat-clock only; no transient deck pitch-bend. |
| 2.8 | Master-deck / clock selection | 🟡 | 🟡 | Leader still hard-coded as "the other deck." `ReapplyRate` (330) |
| **Mixing** |
| 3.1 | Crossfader + curve | ✅ | ✅ | Unchanged. `MixerMath.CrossfaderGains` |
| 3.2 | Channel faders / gain | ✅ | ✅ | Unchanged. |
| 3.3 | 3-band EQ + kill | 🟡 | 🟡 | Unchanged; still no true −∞ kill. |
| 3.4 | Filter sweep | ✅ | ✅ | Unchanged. |
| 3.5 | Trim / auto-gain / ReplayGain | 🔴 | 🔴 | Still none. |
| 3.6 | Headphone cue (PFL) + cue mix | 🔴 | 🟡 | **Bus + controls + cue device built and wired; per-deck pre-listen NOT audible.** Cue mixer carries only the master leg; `BassMixerChannel.SetCue` still only latches a flag (see §3/§4). `CueBusState`, `CueMixMath`, `BassMixerBackend.CreateCueMixerIfConfigured` (195), `MixerActionHandler.ApplyCueLevel/ApplyCueMix` |
| 3.7 | Master out | 🟡 | 🟡 | Now limited (3.8); still default stereo device for master, no master-gain knob. |
| 3.8 | Limiter / clip protection | 🔴 | ✅ | **Closed.** Real stereo-linked brick-wall limiter on the master tap. `Core/Dsp/MasterLimiter.cs`, applied in `BassMixerBackend.OnMasterDsp` (430) |
| **Audio I/O & quality** |
| 4.1 | Low-latency output | 🟡 | 🟡 | Still BASS default output + user buffer; no ASIO/WASAPI-exclusive. |
| 4.2 | Multi-channel (master + cue) routing | 🔴 | 🟡 | **Second cue device now opened** + master leg routed to it; per-deck cue sends not yet summed into it. `BassInitOptions` (cue device), `AudioSettings.CueOutputDeviceId` (41) |
| 4.3 | Sample-rate / resampling | ✅/🟡 | ✅/🟡 | Unchanged. |
| 4.4 | Clipping protection | 🔴 | ✅ | Closed via 3.8. |
| 4.5 | CMD STUDIO 2A device pick / MIDI open | 🟡 | 🟡 | **MIDI now opened into the router** (see 9.1); 4-ch ASIO cue still not; no captured profile. `ServiceConfig.WireMidiInput` (414) |
| **Time-stretch / pitch / key** |
| 5.1 | Keylock quality | 🔴 | 🔴 | Still none. |
| 5.2 | Key shift | 🔴 | 🔴 | Still none. |
| 5.3 | Key detection | ✅ | ✅ | Unchanged. |
| 5.4 | Camelot / harmonic hint | ✅ | ✅ | Unchanged. |
| **Library & metadata** |
| 6.1 | Browse / search / sort | 🟡 | 🟡 | Unchanged depth. |
| 6.2 | Crates / playlists / setlists | 🟡 | 🟡 | Live queue + playlist persistence exist (`JsonPlaylistStore`, `PlaylistAudioPlayer`); still no crates/saved setlists. |
| 6.3 | Tags / BPM / key columns | ✅ | ✅ | Unchanged. |
| 6.4 | History / played log | 🟡 | 🟡 | Unchanged. |
| 6.5 | rekordbox / Serato / iTunes import | 🔴 | 🔴 | Still none. |
| 6.6 | File-format support | ✅ | ✅ | Unchanged. |
| 6.7 | Waveform overview | ✅ | ✅ | Unchanged. |
| 6.8 | Scrolling waveform + beat markers | 🔴 | 🟡 | **Live `WaveformStrip` + `BeatGridCalculator` overlay** in the deck now (from BPM). Still derived from BPM only (no true grid), no scrolling/zoom. `Features/Live/Modules/DeckViewModel` (line 333), `WaveformStrip`, `BeatGridCalculator` |
| **Performance aids** |
| 7.1 | Quantize (snap to grid) | 🟡 | 🟡 | Deck Quantize now does phase-align (see 2.5) but blind on the anchor. |
| 7.2 | Slip mode | 🔴 | 🔴 | None. |
| 7.3 | Censor / reverse | 🔴 | 🔴 | None. |
| 7.4 | FX (echo/reverb/delay) | 🔴 | 🔴 | Still only the mixer filter. |
| 7.5 | Sampler | 🔴 | 🔴 | None. |
| 7.6 | Recording the mix | 🟡 | 🟡 | Master tap exists (now limited); no record-to-file. |
| 7.7 | Streaming | ⚪ | ⚪ | Out of scope. |
| 7.8 | Auto-mix / auto-transition | 🟡 | 🟡 | Autopilot + cue detection exist; DJ-side crossfade driver still unbuilt. |
| **Reliability** |
| 8.1 | Dropout / RT-thread safety | 🟡 | ✅ | **Closed.** Per-buffer `new float[]` removed from the heavy path; reused scratch buffers + grow-only guards. Only the optional master-tap ownership hand-off still allocates (acknowledged). `BassMixerBackend.ApplyChannelDsp/OnMasterDsp/FeedCueMasterLeg` (405–468) |
| 8.2 | End-of-track handling | 🔴 | 🔴 | **Still none** — no end-of-track sync/event on the deck; a deck silently runs out. (no `ChannelEnd`/end-sync in `BassMixerBackend`) |
| 8.3 | Missing-file handling | 🟡 | 🟡 | Unchanged (load throws, caught + logged + rethrown). |
| 8.4 | Crash safety / session restore | 🟡 | 🟡 | `LiveProfileStore`/`ILiveProfileStore` + snapshots now persist profile/playlist/settings; **live deck state (loaded tracks, cues, pitch) not yet snapshotted.** `Media/LiveProfileStore.cs` |
| **Controller integration** |
| 9.1 | CMD STUDIO 2A mapping + MIDI wired | 🟡 | 🟡 | **MIDI input is now opened into `MidiControllerRouter`** at composition — the app is no longer mouse-only. **Still no captured CMD STUDIO 2A profile.** `ServiceConfig.WireMidiInput` (414), `TryOpenMidiPipeline` (427) |
| 9.2 | Jog seek/scratch mapping | 🔴 | 🔴 | Depends on 1.6. |
| 9.3 | LED feedback to controller | ✅/🟡 | ✅ | `MidiFeedbackPublisher` now composed in the MIDI pipeline. `ServiceConfig` (406) |

---

## 3. Critical gaps #1–#5 — closed / partial / open

### #1 Beatgrid first-beat anchor + phase sync — **PARTIALLY CLOSED** 🟡
**In place:** `FirstBeatEstimator` (`Core/Analysis/Bpm/FirstBeatEstimator.cs`) is built and invoked by
`BpmDetector`; `BpmResult` now carries `FirstBeatSeconds` (BpmDetector line 12) and it is stored on
`MusicTrack.Bpm` (`Library/Music/MusicTrack.cs` line 11). The engine exposes
`DeckFirstBeat`/`SetDeckFirstBeat` (`IMultiDeckPlaybackEngine` 67–73), and `SetQuantize` now performs a
real one-shot phase alignment via `PhaseAlignmentCalculator.PhaseNudgeSeconds`
(`TwoDeckBassEngine.PhaseAlignToLeader`, line 383) — no longer a logged no-op.
**What remains:** the anchor never reaches the engine. **`SetDeckFirstBeat` has zero callers** (only a
comment in `DeckActionHandler.LoadTrack`, lines 112–114). `DeckLoadTrack` carries a single `Value` =
BPM, so `FirstBeatSeconds` is dropped at the UI→action boundary (`LibrariesViewModel` 334/351,
`TrackContextActions` 53). Both decks therefore phase-align against `_firstBeat = 0`. To finish:
thread the anchor — either add a `DeckSetFirstBeat` action (or a second numeric field/`Argument`
parse on `DeckLoadTrack`) and have the composition root / load emitters call
`engine.SetDeckFirstBeat(slot, track.Bpm.FirstBeatSeconds)` right after load. No persistence
`IsManual` protection yet.

### #2 Headphone cue (PFL) bus + multi-channel output — **PARTIALLY CLOSED** 🟡
**In place:** `CueBusState` + `CueMixMath.HeadphoneOutputGains` (pure + tested); `MixerCueLevel`/
`MixerCueMix`/`MixerCueToggle` action kinds, handler methods (`MixerActionHandler` 138–189), and live
UI knobs/toggles (`MixerViewModel` 51–61, 131–140). A **second BASS device** for the cue output is
opened (`BassMixerBackend.CreateCueMixerIfConfigured`, 195) driven by `AudioSettings.CueOutputDeviceId`,
and the limited master leg is pushed into it scaled by the cue/master blend (`FeedCueMasterLeg`, 453;
`ICueOutput` wired via `BassMixer.SetCueOutput` ← `TwoDeckBassEngine` ctor, line 117).
**What remains:** the actual **pre-listen of a cued deck is not audible.** `BassMixerChannel.SetCue`
*only latches a flag* (`BassMixerChannel.cs` line 46) — the cued deck's samples are never summed into
the cue mixer. `SetCueOutputGains` explicitly logs that `cueGain` (the cued-deck leg) "is not yet
wired; master leg only" (`BassMixerBackend` 233–244). So today you can blend cue↔master of the
*house* signal, but you cannot monitor a paused/next deck — which is the whole point of PFL. To
finish: add per-deck cue send streams into `_cueMixer` toggled by `SetCue`, and apply `cueGain` to
their sum.

### #3 Loops — **CLOSED** ✅
`DeckSetLoop` is handled (`DeckActionHandler` 68/117), routed to `TwoDeckBassEngine.SetLoop`/`ClearLoop`
(477/512), which sizes the region from base BPM via `BeatLoopCalculator.Region` and installs a BASS
loop via `BassMixerBackend.SetDeckLoop` (323, real `ChannelSetSync`/position loop with empty-span
guards). Feedback (`IsLooping`/`LoopBeats`) flows back, and the deck UI emits a default beat-loop
length (`DeckViewModel` 74–77). Caveat: a loop needs a known base BPM (logged no-op otherwise), and
"musical" loop boundaries inherit the same missing-anchor limitation as #1 (loop starts at the raw
playhead, not snapped to the grid). Functionally complete and reachable.

### #4 Real cue points (settable + persisted hot cues + LED) — **PARTIALLY CLOSED** 🟡
**In place:** hot cues are reachable end-to-end — `DeckHotCue` action → `DeckActionHandler.TriggerHotCue`
(145) → `TwoDeckBassEngine.HotCue` (set-or-jump, 449) with `IsHotCueSet` feedback for pad LEDs; UI has
`HotCuePadViewModel`. The persistence layer was built: `HotCue.cs`, `TrackCueSet.cs`,
`Persistence/IHotCueStore.cs` + `TrackCueRecord.cs`, and `Media/JsonHotCueStore.cs`.
**What remains (important):** **`IHotCueStore`/`JsonHotCueStore` is orphaned** — it has *no callers*
anywhere outside its own definition and is **not registered in `ServiceConfig`**. So hot cues are still
RAM-only and lost on reload/restart, exactly as before; the persistence code is dead until wired. The
temporary `Cue` is also still "jump to track start," not a settable cue (`TwoDeckBassEngine.Cue`, 258).
To finish: register the store in DI and have the engine (or a deck coordinator) load a track's
`TrackCueSet` on `Load` and save on hot-cue set/clear, keyed by track path.

### #5 Master limiter + no RT-thread allocation — **CLOSED** ✅
`Core/Dsp/MasterLimiter.cs` is a real stereo-linked feed-forward brick-wall limiter (−0.1 dBFS ceiling,
1 ms attack / 100 ms release, final hard guard), allocation-free, applied in place on the master tap
(`OnMasterDsp`, 430). The per-buffer `new float[]` was removed from the heavy path: channel DSP, master
limiting, and the cue-leg feed all reuse pre-sized scratch buffers with grow-only logged guards
(`ApplyChannelDsp` 405, `EnsureMasterScratch`/`EnsureChannelScratch`/`EnsureCueLegScratch` 473–501). The
*only* remaining allocation is the master-tap ownership hand-off to the async analysis seam, which is
acknowledged in-code and only occurs when a tap is attached. Done.

---

## 4. New integration seams left dangling (concrete follow-ups)

1. **First-beat anchor never threaded to the engine** (keystone). Nothing calls
   `IMultiDeckPlaybackEngine.SetDeckFirstBeat`. Fix: after each `engine.Load`, call
   `engine.SetDeckFirstBeat(slot, track.Bpm?.FirstBeatSeconds ?? 0)` from the composition root, or
   carry the anchor on the load action (new `DeckSetFirstBeat` kind or a parsed field). Until then
   `TwoDeckBassEngine.PhaseAlignToLeader` (line 383) aligns to anchor 0.
   *Files:* `DeckActionHandler.LoadTrack` (105), `LibrariesViewModel` (334/351), `TrackContextActions` (53).

2. **Hot-cue store not injected.** `JsonHotCueStore` / `IHotCueStore` has no consumers and is absent
   from `ServiceConfig`. Fix: register `IHotCueStore`, and load/save `TrackCueSet` on deck load and on
   hot-cue set/clear. *Files:* `ServiceConfig.cs`, `Media/JsonHotCueStore.cs`, `TwoDeckBassEngine.HotCue` (449).

3. **Per-deck PFL leg not fed.** `BassMixerChannel.SetCue` (line 46) only latches a flag;
   `BassMixerBackend.SetCueOutputGains` ignores `cueGain`. Fix: route each cued deck's samples into
   `_cueMixer` and apply the cue-leg gain. *Files:* `BassMixerChannel.cs`, `BassMixerBackend.cs` (195/233).

4. **No end-of-track handling.** No BASS end sync on decks; needed for auto-cue/warning/auto-advance.
   *File:* `BassMixerBackend` (add `ChannelSetSync` on `BassSync.End`), surface via the engine seam.

5. **Live deck-state not snapshotted.** `LiveProfileStore` persists profile/playlist/settings but not
   loaded decks/cues/pitch — partial vs the doc-20 `LivePerformanceSession` intent. *File:*
   `Media/LiveProfileStore.cs` / `LiveProfileSnapshots.cs`.

---

## 5. Recommended next 3–5 build steps

1. **Thread the first-beat anchor to the engine (closes #1 properly).** Smallest, highest-leverage
   change in the tree: a few lines to call `SetDeckFirstBeat` on load (or a `DeckSetFirstBeat` action),
   plus a test asserting `PhaseAlignToLeader` uses a non-zero anchor. This turns the already-built
   phase-sync from "armed but blind" into the product's headline "one-button sync that actually
   aligns." Add `IsManual` grid protection while here.
2. **Make PFL pre-listen audible (finishes #2).** Sum cued decks' samples into the cue mixer and apply
   `cueGain`; without this the cue feature looks done but a DJ still can't beatmatch in headphones.
3. **Wire the hot-cue store into DI + load/save on the deck (finishes #4 persistence).** Register
   `IHotCueStore`, load `TrackCueSet` on `Load`, save on set/clear. The code already exists — it just
   needs to be reachable.
4. **End-of-track handling + per-deck auto-advance (8.2).** Add a BASS end sync → engine event → live
   queue, so a deck warns / auto-cues instead of running silent.
5. **Capture the CMD STUDIO 2A profile.** MIDI input is now opened into the router; run learn mode on
   the hardware to capture jogs/EQ/faders/cue/hot-cue pads and ship a default profile, turning the
   wired-but-unmapped controller into a real workflow.

> After steps 1–3, the four "partially closed" criticals become fully closed and Liveolator is a
> genuinely usable minimal DJ app: sync that aligns, audible headphone cue, loops, persistent cues,
> and a clean limited master.

---

## 6. Build / test result

- **Build:** `dotnet build -c Debug` → **Build succeeded, 0 warnings, 0 errors.** (BASS native lib
  absent in CI → Live Mode disabled at runtime, as expected; not a build failure.)
- **Tests:** `dotnet test -c Debug --no-build` → **all green, 0 failures.**

| Test assembly | Passed |
|---|---|
| Liveolator.Core.Tests | 545 |
| Liveolator.App.Tests | 175 |
| Liveolator.Audio.Tests | 132 |
| Liveolator.Visuals.Tests | 63 |
| Liveolator.Media.Tests | 61 |
| Liveolator.Midi.Tests | 27 |
| Liveolator.Integration.Tests | 25 |
| Liveolator.Online.Tests | 23 |
| **Total** | **1051** |
