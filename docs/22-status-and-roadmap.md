# 22 — Status Review & Forward Roadmap

> **Purpose:** the systems-analyst + dev-manager status review the owner asked for after the
> multi-agent wave finished. It (a) takes a true snapshot of the integrated tree, (b) does the
> line-up ("יישור קו") across the branch sprawl, and (c) lays out an orderly plan around the
> three stated goals: **DJ decks perfect (primary)**, **music library perfect (parallel)**, and
> **start the visual library**. Date: **2026-06-06**.
>
> Sources of truth cross-checked: `docs/18` (living status), `docs/20` (DJ gap analysis),
> `docs/21-dj-feature-gap-analysis-followup` (post-integration re-audit), and the actual code on
> the integration tip `feat/all-merged`. Where this doc and docs 18/20 disagree, **this doc and
> the follow-up (21) win** — 18/20 predate the merge.

---

## 1. Snapshot — where the project actually is

### The multi-agent wave is done and integrated
All gap-closing agent branches are merged into **`feat/all-merged` (+107 commits over `master`)**.
Every other tip — `feat/app-shell`, `feat/deck-cue-points`, `feat/audio-master-limiter`,
`feat/audio-pfl-cue`, and the six `worktree-agent-*` branches — is fully contained in it
(`0` commits outside). The integration tip builds with **0 warnings** and **1051 tests pass,
0 fail** (independently re-verified in `docs/21-followup §6`).

### What the wave delivered (verified in code, not just commit messages)
- **DJ engine — 4 of 5 doc-20 critical gaps moved forward; two fully closed:**
  - ✅ **Loops** end-to-end (`DeckSetLoop` → `TwoDeckBassEngine.SetLoop` → BASS sync, `BeatLoopCalculator`).
  - ✅ **Master limiter + RT-thread allocation fix** (`Core/Dsp/MasterLimiter.cs`, scratch buffers).
  - ✅ **Beatgrid first-beat anchor + phase-sync** — built (`FirstBeatEstimator`, real `PhaseAlignmentCalculator`) and now **threaded to the engine** via the `DeckSetFirstBeat` action (keystone A1 done — phase-sync aligns to the real downbeat, not a 0 anchor).
  - 🟡 **Headphone cue (PFL) bus** — bus/controls/second output device built, but **per-deck pre-listen not yet audible**.
  - 🟡 **Persistent hot cues** — store written (`JsonHotCueStore`) but **orphaned / not in DI** → still RAM-only.
  - ✅ **MIDI input opened into the live dispatcher** (`ServiceConfig.WireMidiInput`) — app is no longer mouse-only.
  - ✅ **Live waveform UI** + beat-grid overlay + click-to-seek on the deck.
  - ✅ **Runtime audio device selection** + persistence (profiles/playlist/settings).
- **Music library** — production-grade: incremental failure-isolated scan, ATL.NET tags,
  offline BPM/key/Camelot + cues, versioned atomic JSON catalog, Libraries tab, MCP tools. Solid.
- **Visual engine** — scene model complete; compositor grew **multi-layer + blend + live clock**
  ("VJ compositor depth"); visual **catalog** exists (image header probes + ffprobe video probes,
  `scan_visual_folders`/`list_visuals` MCP tools). No visual *browser/editor UI* yet.
- **Extensions** — managed pack spine, signed packages, UI themes, visual-effect registry, audio FX
  racks, VST3 scanner client (native helpers still un-vendored).

### Honest caveats
- **The whole BASS audio path is "verified manually, not in CI."** There is no proof two decks +
  per-deck managed DSP + master tap hold a real-time deadline on the CMD STUDIO 2A. This is the
  single biggest *unknown*, not a known bug.
- **Low-latency story is aspirational** — BASS default output + 10–200 ms buffer, no ASIO/WASAPI-exclusive.
- **Beatgrid is BPM-global** — `FirstBeatEstimator` gives one anchor, no per-section grid / downbeat
  tracking; will drift on variable-tempo material.

---

## 2. Alignment ("יישור קו") — do this FIRST, before any feature work

The biggest current risk is **branch divergence**, not any missing feature. `master` is 107 commits
behind a green, fully-integrated tip, work lives across ~12 branches/worktrees, and the
checked-out `feat/app-shell` working tree has uncommitted edits sitting on a stale base.

| # | Action | Why |
|---|--------|-----|
| 0.1 | **Promote `feat/all-merged` → `master`** (fast-forward; it's +107/-0 and green). | One trustworthy baseline; everyone builds on the integrated reality. |
| 0.2 | **Triage the uncommitted changes in the `feat/app-shell` working tree** — diff vs `feat/all-merged`, keep the genuinely-new edits, discard the already-merged noise. | Avoid losing in-flight work *and* avoid re-introducing reverted churn. |
| 0.3 | **Re-sync stale docs:** update `docs/18` to the merged reality; fold `docs/20` + `docs/21-followup` conclusions into the living status; **fix the doc-21 numbering collision** (two files numbered 21: `21-extension-system` and `21-dj-feature-gap-analysis-followup`). | Docs are the source of truth here; right now they describe a pre-merge world. |
| 0.4 | **Prune merged worktrees/branches** (`.claude/worktrees/*`, `worktree-agent-*`, the per-gap feature branches) once 0.1 lands. | Stop the sprawl from growing; reduce confusion in the next wave. |

> Until 0.1–0.2 are done, treat `feat/all-merged` (worktree `.claude/worktrees/integration`) as the
> real baseline for all measurements below.

---

## 3. Track A — DJ decks to "perfect" (PRIMARY GOAL)

Ordered. Steps A1–A3 are small, high-leverage, and turn three "armed-but-not-wired" features into
genuinely working ones — they are the fastest path to a credible DJ app. The code already exists; it
just isn't reachable.

| # | Step | Effort | What it closes | Key files |
|---|------|--------|----------------|-----------|
| **A1** | ✅ **DONE — first-beat anchor threaded to the engine** (keystone). A new `DeckSetFirstBeat` action carries `BpmResult.FirstBeatSeconds`, emitted right after `DeckLoadTrack` by every load source; `DeckActionHandler` routes it to `engine.SetDeckFirstBeat`, so `TwoDeckBassEngine.PhaseAlignToLeader` now aligns to the real downbeat (no longer a 0 anchor). Tested in `DeckActionHandlerTests` + the emitter tests. *Remaining: per-track `IsManual` grid-edit protection (deferred with manual beatgrid editing).* | S | Makes phase-sync actually align beats → the headline "one-button sync." Unblocks musical loops & on-grid cues & beat-synced visuals. | `PerformanceActionKind.DeckSetFirstBeat`, `DeckActionHandler`, `LibrariesViewModel`, `TrackContextActions`/`TrackMenuViewModel`, `TwoDeckBassEngine.PhaseAlignToLeader` |
| **A2** | **Make PFL pre-listen audible.** Sum each cued deck's samples into `_cueMixer` toggled by `SetCue`, apply `cueGain` (currently `BassMixerChannel.SetCue` only latches a flag; `SetCueOutputGains` ignores the cued-deck leg). | M | You cannot beatmatch in headphones without this — the whole point of the CMD STUDIO 2A 4-ch interface. | `BassMixerChannel.cs`, `BassMixerBackend.cs` (195/233) |
| **A3** | **Wire the hot-cue store into DI + load/save on the deck.** Register `IHotCueStore`; load a track's `TrackCueSet` on `Load`, save on set/clear, keyed by path. | S | Cues survive reload/restart — the code exists but is dead until reachable. | `ServiceConfig.cs`, `Media/JsonHotCueStore.cs`, `TwoDeckBassEngine.HotCue` |
| **A4** | **End-of-track handling + per-deck auto-advance.** Add a BASS end-sync → engine event → live queue (warn / auto-cue / stop). | S–M | Today a deck silently runs out — unacceptable live. | `BassMixerBackend` (`ChannelSetSync` on `BassSync.End`), engine seam, `LivePlaylist` |
| **A5** | **Settable temporary cue** (not just "jump to track start") + cue-play-hold. | S | Standard CDJ vocabulary. | `TwoDeckBassEngine.Cue` |
| **A6** | **Live session snapshot/restore** — loaded decks, cues, pitch, queue. | M | Crash safety; the `LiveProfileStore` already persists everything *but* deck state. | `Media/LiveProfileStore.cs` |
| **A7** | **🔴 Real-hardware BASS verification on the CMD STUDIO 2A** — two decks + DSP + master tap holding the RT deadline; measure latency; verify the second cue device. | M | Retires the #1 unknown. Should run *in parallel* with A1–A3, not after. | manual checklist (`docs/01`) |
| **A8** | **Capture the CMD STUDIO 2A MIDI profile** via learn mode (jogs/EQ/faders/cue/hot-cue pads); ship default profile. | S+S | Turns the wired-but-unmapped controller into a real workflow. | `docs/07`, mapping engine |

**Important-but-later (after A1–A4):** keylock / master-tempo (needs `ManagedBass.Fx`), scrolling/zoomed
playing waveform with true beat markers, EQ true-kill (−∞), ASIO/WASAPI-exclusive low-latency path,
explicit master-deck selection, ReplayGain/auto-gain, auto-mix transition driver.

**"Perfect decks" exit criteria:** sync that *aligns* (A1), audible headphone cue (A2), persistent cues
(A3), graceful end-of-track (A4), proven on real hardware (A7), driven from the CMD STUDIO 2A (A8).

---

## 4. Track B — Music library to "perfect" (PARALLEL)

The library is already strong; "perfect" here is mostly **surfacing built backend in the UI** and
closing two analysis stubs. Independent of Track A — can run concurrently.

| # | Step | Effort | Note |
|---|------|--------|------|
| **B1** | **Search / filter / sort UI.** Wire the existing `TrackFacets` (artists/genres/years/types — built, unused) into facet dropdowns; add sort by BPM/key/duration; add a status filter (Ok / Partial / Failed). | M | Backend exists; this is pure UI. Biggest perceived-quality win. |
| **B2** | **Sample-folder designation UI.** Expose `SetSampleFolders()` (backend + persistence done) in the Folders window. | S | Closes the one user-facing gap in sample classification. |
| **B3** | **Phrase-boundary cues** — `IntroEnd` / `OutroStart` (currently `null`; needs a phrase/energy analyzer beyond silence detection). | M | Improves auto-mix and on-grid cueing; depends on energy-section DSP. |
| **B4** | **Activate + integration-test online enrichment.** Provide API keys (GetSongBPM/AcoustID), add a live-path integration test. | S–M | Code is wired (`OnlineMetadataProvider` + merge policy); just inert without keys. |
| **B5** | **Crates / saved setlists + played-history surfacing.** | M | `TrackState.Played` modeled but not shown; no crates yet. |

**Later adoption lever:** rekordbox / Serato / iTunes library import (reuse their cue/grid data).

**"Perfect library" exit criteria:** scan→analyze→browse with real filter/sort/facets, sample
designation, both intro/outro cue pairs, optional online enrichment on, setlists + history.

---

## 5. Track C — Start the visual library

The foundation is genuinely solid (scene model, multi-layer compositor, live clock, asset catalog,
MCP tools). The gaps are **UI + orchestration**, not architecture. Recommended start order:

| # | Step | Effort | Note |
|---|------|--------|------|
| **C1** | **Visual asset browser UI** — mirror the music Libraries tab over the existing visual catalog (`VisualMediaLibrary`, `scan_visual_folders`/`list_visuals`): browse/search/filter images + videos, thumbnails. | M | Highest ROI; reuses the proven music-library UI pattern. There is *no* visual UI today. |
| **C2** | **Scene / layer editor UI** — build a scene: layer stack, source picker (from C1's browser), blend-mode + opacity, beat behavior, macro bindings. Persist banks. | L | The only way to author content without hand-editing JSON. |
| **C3** | **Bank persistence + bank browser** — banks are currently hardcoded (4 tabs). Load/save via `ILiveProfileStore` and pick at runtime. | S–M | Wires `VisualSelectBank` to real data. |
| **C4** | **Verify + finish effect-chain execution in the compositor** — confirm what "compositor depth" actually renders; load/cache GLSL per `EffectRef`, drive uniforms from macros. | M | Turns the effect/macro vocabulary into visible output. |
| **C5** | **Video + camera sources** — FFmpeg frame decode → texture (play/loop/scrub); camera device enumeration + capture. | L | `VisualSourceKind.VideoClip`/`Camera` modeled; `VisualSetLayerSource` action still unclaimable until the payload carries a `VisualSourceRef`. |
| **C6** | **Macro → parameter binding UI** + Push-encoder mapping for live visual control. | M | Closes the loop to beat-synced live VJ — the product's strategic differentiator. |

**First milestone for the visual library:** C1 + C3 + a thin slice of C2 — browse assets, pick a
clip into a layer, save a bank, see it on screen via the live clock.

---

## 6. Sequencing & ownership

```
NOW ── Alignment 0.1–0.4 (consolidate to master, triage, re-sync docs, prune)
        │
        ├── Track A (PRIMARY): A1 → A2 → A3 → A4 → A5/A6     ┐
        │      ‖ in parallel: A7 hardware verify, A8 profile │ DJ owner
        │                                                    ┘
        ├── Track B (parallel): B1 → B2 → B4 → B3/B5          ┘ Library owner
        │
        └── Track C (start): C1 → C3 → C2(slice) → C4 → C5/C6   VJ owner
```

- **Gate:** finish Alignment §2 before opening new feature branches, so the next wave forks from one
  green baseline (avoids re-creating today's sprawl).
- **Per the parallel-agent workflow:** each track on its own branch/worktree; **verify build + full
  test suite green before merge**; don't touch foreign files.
- **Definition of done for any step:** TDD (test first), reachable through the action/dispatcher seam,
  no allocation on the audio thread, docs/18 updated.

---

## 7. Top risks to watch

1. **Unproven real-time audio on hardware (A7)** — could force buffer/ASIO rework; verify early.
2. **Beatgrid accuracy** — global-BPM anchor will drift on variable-tempo tracks; A1 makes it *used*,
   not *better*. A richer per-section grid is a later algorithmic effort.
3. **Branch sprawl regrowth** — mitigated by the §2 gate.
4. **Native distribution artifacts** still un-vendored (BASS, BASSmix, VST3 scanner/bridge,
   shader-probe) — packaging/licensing work that blocks a shippable build, independent of features.
</content>
</invoke>
