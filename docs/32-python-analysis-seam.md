# 32 — Python analysis seam: stems + structure segmentation (work plan)

> Status: **PLAN / pending owner sign-off on the blocking decisions in §2.**
> Decided via the advisors gate (2026-06-29, `dj-software-advisor` + code inventory).
> Builds on the licensing analysis and the existing pure-C# analysis brain (doc 16).

## 1. Goal & guardrails

Add an **offline Python subprocess seam** to Liveolator — the same architectural slot the
FFmpeg and `fpcalc` CLIs already occupy. Python is invoked at **import/analysis time only**,
behind a Core interface, and its output is cached to the catalog. It **never** runs on the
realtime BASS path and **never** lives inside the pure-C# `Liveolator.Core`.

Two capabilities justify it (priority order):

| Pri | Feature | Library | License | Why Python |
|-----|---------|---------|---------|-----------|
| **P1** | Song-structure segmentation (intro/drop/breakdown/outro) | **librosa** | ISC (clean) | Robust boundary detection we don't have in C# |
| **P0 value, P2 effort** | Stem separation (drums/bass/vocals/other) | **Open-Unmix** (default) / Demucs (opt-in) | MIT / CC-BY-NC weights | No realistic pure-.NET path |

**Sequencing decision:** build **segmentation (P1) FIRST**. It is license-clean, cheap
(no PyTorch, no GB-scale model), and it validates the entire Python seam — subprocess
contract, runtime packaging, output caching, re-analysis versioning — on a low-risk feature.
Stems (the heavier, license-sensitive, storage-heavy feature) reuses that proven seam.

**What we explicitly do NOT do** (advisors' skeptical verdict):
- Do **not** rewrite the C# beat grid (`BpmDetector`/`PercussiveOnsetEnvelope`/`DownbeatEstimator`/
  `GridRefiner`) or key detection (`ChromaExtractor`+`KeyClassifier`) in Python — both are at
  parity with reference methods for club material; a rewrite adds a 2nd impl, breaks the
  pure-Core test guarantee, and drags in non-permissive licenses.
- Do **not** use Essentia (AGPL → incompatible with a closed-source distributed app).
- LUFS/BS.1770 metering is a real gap but is **pure C#**, tracked separately — not part of this.

## 2. Blocking decisions (owner) — needed before code

1. **Python runtime packaging.** ✅ **DECIDED (2026-06-29): option (c) — download on demand.**
   Ship without Python; an in-app "Enable advanced analysis" one-click download fetches a
   pinned runtime into `%APPDATA%\Liveolator\python`. Base installer stays small; advanced
   analysis degrades gracefully when the runtime is absent (mirrors "Live Mode disabled if
   BASS native missing"). The `PythonRuntime` resolver therefore points at the per-user dir,
   and the analysis path is a no-op (return null + log) until the runtime is present.
2. **Stems model.** ✅ **DECIDED (2026-06-29): model-agnostic seam; Open-Unmix (MIT code+weights)
   is the bundled default; htdemucs (CC-BY-NC weights) is an opt-in model the user downloads
   themselves.** Zero license risk in the shipped default; the higher-quality model is the user's
   choice, never distributed by us.
3. **Stem storage.** ✅ **DECIDED (2026-06-29): FLAC sidecars (4 per track), with a MANDATORY local
   cache before a deck load.** Stems may live next to the source / in the catalog, but are always
   copied locally before being handed to a deck — never decode stems from the S: network drive on
   the load path during a show.

## 3. Architecture (the seam)

```
Liveolator.Core (pure C#)
  Analysis/Structure/ISongStructureAnalyzer.cs   ← interface (the seam)
  Analysis/Structure/SongStructure.cs            ← result DTO (sections + boundaries)
  Analysis/Stems/IStemSeparator.cs               ← interface (phase B)

Liveolator.Media (or new Liveolator.Python)      ← subprocess impls, mirrors FFmpeg/fpcalc
  PythonRuntime.cs                               ← resolves the interpreter (decision §2.1)
  PythonSongStructureAnalyzer : ISongStructureAnalyzer
  scripts/analyze_structure.py                   ← librosa; reads wav path, writes JSON
  DemucsStemSeparator : IStemSeparator           ← phase B
  scripts/separate_stems.py                      ← phase B

TrackAnalyzer (Core)                             ← orchestrates; feeds AutoCuePlacer (existing)
```

Contract = JSON over stdout, exactly like `fpcalc -json`. Missing runtime / non-zero exit /
parse failure → log + return null (graceful, never throws on the analysis path).

## 4. Phased work plan (TDD, small safe steps)

### Phase 0 — Seam skeleton + packaging spike  *(blocked on §2.1)*
- `ISongStructureAnalyzer` + `SongStructure` DTO in Core (pure, unit-testable with a fake).
- `PythonRuntime` resolver per the §2.1 decision; integration test that detects/launches Python.
- Proves the subprocess contract end-to-end with a trivial echo script.

### Phase 1 — Segmentation (P1, the validating slice)
- `analyze_structure.py`: librosa laplacian/novelty segmentation → JSON (boundaries + labels).
- `PythonSongStructureAnalyzer` (subprocess impl) + integration test against real WAV fixtures.
- Extend `TrackAnalysisResult` with `Sections`; bump `TrackAnalyzer.CurrentVersion` → 5
  (auto re-analysis on next scan — mechanism already exists).
- **Wire into the existing C# `AutoCuePlacer`** so hot cues land on real section boundaries
  (the highest-value payoff; no rewrite of the placer).
- Optional UI: colored structure bands on the waveform strip.

### Phase 2 — Stems (P0 value)  *(blocked on §2.2, §2.3)*
- `separate_stems.py` (Open-Unmix default, model-agnostic) → FLAC sidecars + manifest.
- `DemucsStemSeparator : IStemSeparator`; cache to local store, never to S: on the load path.
- Deck loading of 4 stem streams → verify BASS mixer headroom (4× channels/deck) under load.
- `PerformanceAction`s for per-stem mute/isolate; map to Push pads / CMD knobs.
- Bonus: feed the drums stem into `TrackAnalyzer` for a cleaner onset envelope on busy mixes.

### Phase 2 status & 2b design (chosen 2026-06-30, dj-software-advisor over the real BASS code)
- **2a (offline separation) — DONE** on branch `feat/stems` (commit e751e76): `IStemSeparator` +
  `StemSet`/`StemKind` (Core), `OpenUnmixStemSeparator` + `StemStore` (local FLAC cache, SHA-256 key) +
  `separate_stems.py` (Open-Unmix umxhq), installer provisions openunmix+soundfile. Core 1371 + Media 244 green.
- **2b (realtime playback) — DESIGN APPROVED, build pending.** Architecture = **Option C: a per-deck
  "stem submix"** — 4 FLAC decoders → one decode `BASS_Mixer` → wrapped in the existing BASS_FX tempo
  stream → plugged into the master mixer exactly where the single file stream is today. Stems are
  sample-locked BY CONSTRUCTION (one clock); the whole transport/EQ/filter/crossfader/cue/limiter surface
  is inherited unchanged. Only **seek** and **loop-wrap** must additionally reposition the 4 inner stems
  (same fraction → same byte offset). Per-stem mute/isolate = `MixerStemEnable` PerformanceAction →
  `IMixer.SetStemEnabled` → `Bass.ChannelSetAttribute(Volume)` on the control thread (BASS-ramped, zero
  audio-thread work). DSP does NOT multiply (runs once post-sum). Default-off "Stems" gate; single mixed
  file fallback when stems absent/incomplete/not-cached. Build sub-slices, ranked:
  1. **Native spike** (`OpenStemDeck`, no UI/actions) — prove mixer-in-mixer reports position through
     BASS_FX, inner-stem seek stays locked, END fires. **HARDWARE-VERIFIED BY OWNER** (no BASS in CI).
     **BUILT 2026-06-30** behind default-off env gate `LIVEOLATOR_STEMS=1`: `BassMixerBackend.OpenStemDeck`
     (4 FLAC decoders → one decode `BASS_Mixer` submix, returned as the deck handle so BASS_FX/master are
     unchanged), stem-aware seek + loop-wrap via `SeekStemDecodersToFraction` (same fraction → same byte
     offset), stem-aware free in `UnplugDeck`/`Dispose`. Core seam `IStemCache` (impl = `StemStore`); Load
     branch `TwoDeckBassEngine.OpenDeckHandle` chooses stems only on gate-on + complete + local cached set,
     else single file, with a single-attempt single-file fallback if the stem open throws. Pure gate logic
     in `StemDeckDecision`. Managed logic unit-tested (Audio.Tests); the three native unknowns below remain
     owner-verified on hardware.
  2. **Per-stem MUTE — BUILT 2026-07-09 (slice 2), tested (Core+Audio+App green).** First owner-visible
     demo. Advisors (dj-software-advisor over the real code) chose per-stem MUTE (not isolate/solo) and a
     key **architecture change from the original plan: a `DeckStemMute` Deck action (Slot=A/B, Argument=stem
     name), NOT `MixerStemEnable` via `IMixer`.** Reason: the inner decoders live in the backend keyed by
     handle (which the engine owns) and the per-slot `DeckSlot` already resets track state on load, so
     mute is reset-on-load "for free"; `MixerActionHandler`/`BassMixer` are slot-addressed with no load
     hook. Modelled on `DeckKeyLockToggle`. Touch points delivered: `PerformanceActionKind.DeckStemMute`;
     `DeckActionHandler` (toggle + per-stem feedback + relight-all-4-on-load, feedback IsActive = AUDIBLE,
     IsAvailable = stem deck); `IMultiDeckPlaybackEngine.IsStemDeck/IsStemMuted/SetStemMuted`;
     `DeckSlot.IsStemDeck` + `bool[4] StemMuted` (cleared in `UnloadSlot`); `TwoDeckBassEngine.Stems.cs`
     (state, reset-on-load, single-file no-op) + `OpenDeckHandle` reports stem-ness;
     `IBassMixerBackend.SetStemEnabled(handle, kind, enabled)` → `BassMixerBackend`
     `Bass.ChannelSlideAttribute(innerDecoder, Volume, 0|1, 20 ms)` (click-free ramp, zero audio-thread
     work; decoder resolved via new `StemSet.IndexOf(kind)`); UI = `StemMuteViewModel` + a per-channel
     `STEMS` 2×2 cluster on the DJ mixer channel strip (`DjMixerView.axaml`, both decks, lit = audible,
     enabled only for a stem deck). **NATIVE owner-verify remaining:** that a Volume slide on a submix
     source decoder attenuates just that stem, click-free (not exercised in CI).
  3. **Gate promotion + in-app stem generation — BUILT 2026-07-09 (slice 3), tested (Core 1414 / Media 248 /
     App 898 green).** Advisor-gated (dj-software-advisor, "full usability" scope). Made stems usable without
     dev tooling: (a) the gate moved from the `LIVEOLATOR_STEMS=1` env var to a persisted, default-off
     `AudioSettings.StemsEnabled` (Settings → Extensions checkbox, next to "Enable advanced analysis";
     read once at engine construction → **takes effect on next launch**; env var kept as a dev OR-override).
     Plumbed through the flat `SettingsSnapshot` (trailing nullable param, no version bump). (b) `IStemSeparator`
     → `OpenUnmixStemSeparator` registered in DI (reuses the existing `PythonRuntime` + `StemStore`; the
     advanced-analysis installer already provisions openunmix+soundfile). (c) a "Separate stems (experimental)"
     per-track action — `TrackContextActions.SeparateStemsAsync`/`CanSeparateStems` (mirrors `AutoCueAsync`),
     surfaced as `TrackMenuViewModel.SeparateStemsCommand` in the **library track context menu** (2 spots).
     Honest state messages for runtime-absent / offline-source / done / failed, a "several minutes, heavy CPU —
     before your set" pre-warning, and an in-flight `HashSet` guard against concurrent separation of the same
     track (would corrupt the FLACs). **Deck-view generation button DEFERRED** (a multi-minute CPU job doesn't
     belong on a live deck — advisor's own "prep-time, not live-set" rule; the library menu is the prep surface).
     Also deferred: batch separation, htdemucs opt-in, cache eviction/size-cap/content-hash keying, isolate/EQ,
     Push/CMD mapping, live gate toggle. End-to-end path now: enable advanced analysis → right-click "Separate
     stems" → tick "Stem decks" → restart → load → the slice-2 STEMS mute buttons work.
  4. Isolate UX + Push/CMD feedback + mid-track toggle.
  5. (later) per-stem EQ/filter — deferred (would multiply DSP).
  Touch points: `BassMixerBackend.cs` (OpenStemDeck, stem-aware seek/loop/free, SetStemEnabled),
  `IBassMixerBackend`/`IBassMixerChannel`/`BassMixer.cs`, `TwoDeckBassEngine.Transport.cs` (load branch),
  `IMixer.cs`/`MixerActionHandler.cs`/`PerformanceActionKind.cs` (MixerStemEnable).

### Phase 3 — Hardening
- Real-audio regression corpus (also needed by beat-sync) to measure segmentation/stem quality.
- THIRD-PARTY-NOTICES updated for every new Python dep + model.
- Installer size / first-run download UX; uninstall cleanup of the Python dir.

## 5. Team / roles

| Role | Who | Owns |
|------|-----|------|
| **Advisors (gate)** | `dj-software-advisor`, `system-gap-review` | UX of stem isolation, quality bar, license sign-off framing — **done for Phase 0** |
| **Seam + Python impl** | `add-mcp-tool`-style seam discipline + dev-standards | `ISongStructureAnalyzer`/`IStemSeparator`, subprocess impls, Python scripts |
| **Core integration** | `add-performance-action` (stem mute/isolate actions), `add-beat-engine-feature` if grid consumes stems | `TrackAnalysisResult`, version bump, `AutoCuePlacer` wiring |
| **Controller mapping** | `add-controller-mapping` | per-stem pads/knobs on Push + CMD |
| **Audio stability** | `dj-software-advisor` + `qa-engineer` | 4-channel-per-deck mixer headroom, network-drive load safety |
| **QA / verify** | `qa-engineer` | build+run, reproduce, regression corpus |

Parallel work uses the worktree-per-agent flow (`scripts/new-agent-worktree.ps1` +
serialized merge gate), not the shared tree.

## 6. Risks
- **Installer bloat / PyTorch (stems):** ~1.5–3 GB. Mitigated by §2.1(c) optional download.
- **Two-language maintenance:** quarantined to one subprocess seam; Core stays pure C#.
- **Stem storage on S::** mandatory local cache before deck load.
- **Weights license (htdemucs CC-BY-NC):** default to Open-Unmix MIT; htdemucs opt-in only.
