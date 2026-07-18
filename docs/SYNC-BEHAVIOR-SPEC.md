# Sync Behavior Specification (Spec / Proposed)

> Status: **proposed** — the contract we build to, not yet fully implemented. Owner
> approval of the **Deliberate decisions** (§12) is the gate before any code.
>
> Scope: how Liveolator's two-deck sync must behave to reach VirtualDJ / rekordbox /
> Serato / Traktor / djay / Mixxx quality. Companion to [`docs/11`](11-deck-ab-pro-dj.md)
> (deck engine) and [`docs/03`](03-beat-engine.md) (shared beat clock). Pure-math seams
> live in `src/Liveolator.Core/Audio/Sync/`; the engine surface in
> `src/Liveolator.Audio/Playback/TwoDeckBassEngine.Sync.cs` + `.Tempo.cs`.

## 1. Why this spec exists

Sync is not "match BPM and move the playhead." It is a **system**: grid analysis →
master election → a chosen sync *mode* with an explicit guarantee → a continuous,
inaudible lock → honest UI. Today we fix symptoms one bug at a time because there is no
single written contract. This document is that contract: a behavior table per mode, a
master-election policy, a phase-correction policy, a grid-quality gate, and a set of
**musical acceptance tests** a pro DJ would use to judge each mode. After it is approved,
development is sharp instead of a bug hunt.

## 2. Current state (honest baseline)

Liveolator's engine is **already well past** "match BPM + nudge." What exists and is
good (keep it):

| Capability | Where | Notes |
|---|---|---|
| Continuous phase-lock loop | `PhaseLockController.Correct` | Proportional, memoryless (no integral windup → no drift accumulation), lock-zone hysteresis, re-snap escape hatch. This **is** the Sync-Lock engine. |
| One-shot beatmatch | `TwoDeckBassEngine.SyncOnce` | Tempo match + one phase snap + key-lock-on. Does not latch. |
| Continuous latch | `SetSyncLock` / `UpdateSync` / `CorrectSlaveLocked` | The SYNC button today. |
| Half/double fold | `TempoSyncCalculator.RateFor` | Folds ½×/2× at the √2 geometric midpoint (70 follows 140 at ≈1.0×). |
| Sync-stretch ceiling | `SyncRangePercent = 0.35` (±35%) | Reports `OutOfRange` instead of a chipmunk pitch. Manual pitch fader is `PitchRangePercent = 0.08` (±8%). |
| Lock-state model | `SyncLockState` = `Off/Active/Locked/Drifting/OutOfRange` | Pushed to UI via `SyncStateChanged` (outside the audio gate). |
| Soft/auto master | `ComputeSyncMasterLocked` / `ValidLeaderSlot` | Master = the valid leader of the synced deck; a **paused deck is refused as master** (correct — never lock to a frozen grid). |
| Key lock preserves pitch | `BassMixerBackend.ApplyRate` (native) | Key-lock ON → BASS_FX time-stretch (`Tempo` %); OFF → vinyl resample. Auto-engaged before any sync rate is applied. |
| Bar-vs-beat snap | `PhaseAlignToLeader` | Bar-snaps onto the leader downbeat only when both downbeats known **and** the follower is not playing; else beat-snaps. |
| Half/double badge infra | `BpmMatch.OctaveFactor` → `PerformanceDeckSet.OctaveLabel` | A `½×`/`2×` label exists — but is wired to the *matched-highlight* (both decks playing), **not** to the active sync fold. |

### The real gaps this spec drives (all verified in code)

1. **One behavior, not three.** Only a latch + a one-shot exist. No distinct **Tempo Sync**
   (tempo-only, no phase) and no cleanly separated one-shot **Beat Sync** as its own mode.
2. **No grid-confidence gate.** `SyncOnce`/`SetSyncLock` proceed on any `BaseBpm > 0`. A
   wrong/low-confidence grid produces a confidently-wrong lock. `GridRefiner.Coherence` is
   *computed and then discarded* — it never reaches `BpmResult`, sync, or the UI.
3. **No manual MASTER** and **no external master.** Election is 100% automatic;
   `BeatClockSource.External` (Ableton Link) is declared but unimplemented.
4. **No armed / quantized start.** SYNC on a stopped deck logs "no live leader" and no-ops;
   Play does not enter on the next bar. Quantize is a **one-shot snap on enable**, not a
   persistent "snap every trigger" mode, and has **no button** in the deck UI.
5. **Lock sub-states invisible.** UI renders only `OutOfRange` vs not; `Active`/`Locked`/
   `Drifting` are not shown. No master-vs-follower indicator. Sync-fold `2×`/`½×` not shown.
6. **No manual nudge of a synced deck.** `PitchBend`/`SetPitch` bail out while `SyncLocked`.

Minor: `DeckSlot.KeyLocked` docstring is stale (claims "intent only"; key-lock is native).
`BeatsPerBar = 4` is hardcoded in the sync path.

## 3. Glossary (one definition per term)

- **Master / leader** — the deck (or external clock) whose grid everyone else follows. Its
  timing is never altered by sync.
- **Follower / slave** — a deck whose rate (and optionally phase) is driven to the master.
- **Beatmatch / tempo match** — making the follower's BPM equal the master's (a rate change).
- **Phase align** — moving the follower's playhead so its beat (ideally downbeat) lands on
  the master's beat.
- **Lock zone** — the beat-phase error band inside which the decks are audibly in sync and
  no correction is applied.
- **Re-snap** — a one-shot seek onto the nearest aligned beat, used only after a
  discontinuity (scratch / beat-jump / loop-out) too large to ride back on pitch.
- **Grid confidence** — a 0..1 measure of how trustworthy a track's beatgrid is; gates
  whether phase sync is offered (§7).

## 4. Sync mode taxonomy + behavior table

Three modes with **different, visible guarantees**. The cardinal rule: these are distinct
modes, not one "SYNC" that silently means different things (the #1 Traktor confusion).

- **Tempo Sync** — tempo-only, no phase. For manual beatmatchers, off-beat/creative mixing,
  and the safe fallback when the grid is untrustworthy. *(Traktor TempoSync; Serato Simple
  Sync — needs no beatgrid; djay BPM Sync.)*
- **Beat Sync** — tempo **+ one-shot** phase/downbeat align, then hands off. The everyday
  "get me in the ballpark, I'll ride it." *(rekordbox Beat Sync; djay BPM + Beat Sync;
  Traktor BeatSync press.)*
- **Sync Lock** — Beat Sync **held continuously**: rate + phase glued to the master forever,
  survives scratches/jumps/loops (re-locks after the gesture). For hands-off blends,
  harmonic layering, 3–4-deck stacks. *(Mixxx Sync Lock; Serato Smart Sync; Traktor latched
  BeatSync.)*

### Behavior table

| Property | Off | Tempo Sync | Beat Sync (one-shot) | Sync Lock (continuous) |
|---|---|---|---|---|
| Match follower BPM to master | – | ✅ | ✅ | ✅ |
| Align beat phase | – | ❌ (DJ does it) | ✅ once, at engage/start | ✅ continuously |
| Align to **bar** (downbeat) | – | ❌ | ✅ if downbeat known + not playing | ✅ established at engage, held on beat grid |
| Continuous correction loop | – | ❌ | ❌ | ✅ (`PhaseLockController`) |
| Survives scratch / beat-jump / loop | – | n/a | ❌ (does not re-lock) | ✅ (bend, then re-snap) |
| Follows master tempo changes live | – | ✅ | ❌ (one-shot only) | ✅ |
| Manual pitch-bend allowed while engaged | ✅ | ✅ | ✅ | ❌ locked — drop to Tempo Sync to bend (§12-3) |
| Requires a valid (playing) master | – | ✅ | ✅ | ✅ |
| Grid-confidence required (§7) | – | ❌ (tempo needs BPM only) | ✅ high, else downgrade to Tempo | ✅ high, else downgrade to Tempo |
| Key lock auto-engaged | – | ✅ | ✅ | ✅ |
| Lock states reachable | `Off` | `Active` (tempo held) / `OutOfRange` | transient (`Active`→`Off` after snap) / `OutOfRange` | `Active`/`Locked`/`Drifting`/`OutOfRange` |

**Engine mapping (build on what exists, don't rebuild):**
- Tempo Sync → apply `TempoSyncCalculator` rate only; **skip** `PhaseAlignToLeader`; do
  **not** run `CorrectSlaveLocked`.
- Beat Sync → existing `SyncOnce` (tempo + one `PhaseAlignToLeader`), do **not** latch.
- Sync Lock → existing `SetSyncLock` + the `CorrectSlaveLocked` loop.

**Quantize** is a *modifier*, not a mode: when on, a deck's **start/hot-cue/loop-in** enter
on the next grid boundary (§6). Today it is only a one-shot phase snap — see §6 gap.

## 5. Master / leader election policy

Keep today's **soft/auto** leader as default (matches Mixxx, safest for two decks); add a
manual pin and an external-master hook.

**Default (Auto / soft):** master = the other deck iff *loaded, playing, not itself synced,
known base BPM* — exactly `ValidLeaderSlot`. A **paused deck is never master**. Keep both.

**Manual MASTER pin:** a pinned deck stays master even if the other starts, until unpinned,
unloaded, or ended. A pinned master that **stops** must **warn and fall back to Auto** —
never hold followers hostage against a dead grid.

**External master (Ableton Link):** when Link is enabled the **shared beat clock is the
master** and both decks follow it. This fits our "one shared beat clock" architecture
better than any product's bolt-on — make Link a first-class master. `TryGetSyncMasterBeat`
already publishes a continuous master beat; the Link path inverts it so an external phase
*drives* the grid.

### Master-transition table (the critical part)

| Event | Follower must… | Master role… |
|---|---|---|
| Master **pauses** | keep own tempo, do **not** lock to frozen grid, hold `Active` | invalid until resume; no re-lock |
| Master **ends** (track finishes) | keep playing at current tempo — **no freeze, no jump** | re-elect if another valid master exists, else release sync |
| Master **unloaded** | keep playing steadily; SYNC must not wedge | cleared; master badge clears |
| Master **BPM changes** (new track / pitch move) | follow continuously; **ramp** rate over ~1 bar, never step | unchanged |
| Follower **stops** | – | its influence ends; leader may migrate to a still-playing deck |
| **No valid master** while armed | hold own tempo, `Active`; snap on the first tick a master becomes valid | none until one is valid |

> Rule of thumb everywhere: **re-elect first, release only if no valid master remains.**
> Written this way it stays correct when 4 decks land (already in STUDIO scope).

## 6. Armed / quantized start

The scenario that makes naive sync feel broken: a **stopped** deck, DJ presses SYNC then PLAY.

1. **SYNC on a stopped, loaded deck with a valid master** → apply tempo match immediately
   (BPM readout updates — the button is not dead), set `Active`, and **arm** a phase align.
   Sync intent must **arm, not no-op** (today it no-ops — the core armed-start gap).
2. **PLAY on an armed synced deck** → if Quantize on, defer the start to the next grid
   boundary and snap phase there; if off, start now and let the loop pull it in.
3. **Quantize granularity** = setting: `Beat` (tight cutting) or `Bar` (guarantees the "one"
   lines up). Default **Bar** for Sync Lock, **Beat** for manual/cut workflows. Reuse the
   existing `PhaseAlignToLeader` bar-vs-beat predicate (bar-snap when both downbeats known +
   deck not playing).
4. **No valid master when armed** → keep tempo, hold `Active`, snap on the first valid-master
   tick. Never a wrong tempo (the existing `ReapplyRate` philosophy).

> Quantize must also become the persistent "snap every trigger to the grid" modifier
> (hot-cue / loop-in / play), like rekordbox/Serato — today it is only a one-shot snap on
> enable and has no deck-UI button.

## 7. Beatgrid quality gate (highest-leverage new piece)

Sync is only as good as the grid. Serato's whole Simple-vs-Smart split exists because Smart
Sync requires accurate beatgrids and Simple Sync deliberately needs none. Liveolator has
**no gate today** — the single most important correctness addition.

| Grid confidence | Beat Sync / Sync Lock | Tempo Sync | UI |
|---|---|---|---|
| **High** | full phase align available | available | solid grid indicator |
| **Low / uncertain** | **downgrade to Tempo Sync** (match BPM, leave phase to the DJ) | available | **"grid uncertain"** (hollow/striped) |
| **None / unanalyzed** | refuse phase sync | tempo-only if a BPM estimate exists | "not analyzed" |

**Never silently half-align to a bad grid** — that is the root cause of "sync sounds wrong."
Downgrade honestly instead. A false "Locked" that drifts on a full floor costs far more than
an unnecessary tempo-only downgrade, so the gate is **asymmetric — biased toward downgrade.**

### Confidence model (DECIDED — research-backed, 2026-07-17)

The industry has **no published confidence number** (vendors gate via mode + fallback — Serato
Simple-vs-Smart is exactly this offer/downgrade split; rekordbox/Traktor/Engine use static-vs-
dynamic grids + a grid-lock flag). The **academic consensus** is convergent and is what we
adopt: measure confidence as **committee / mutual agreement** (multiple estimators scored by the
information-gain beat measure) **plus a constant-tempo-across-track proof**, and threshold
conservatively. Anchors: Essentia's beat-tracker `confidence` (mutual agreement via information
gain; **"good" ≈ ≥1.5 bits ⇒ ~80% accuracy**) and **Zapata et al.'s ~1.5-bit acceptability
threshold** (~73% of music is "trackable"). Sources: Essentia `BeatTrackerMultiFeature`,
`RhythmExtractor2013`; Zapata *Assigning a Confidence Threshold* (ISMIR 2012); Davies
*Evaluating the Evaluation Measures* (ISMIR 2014); Serato Simple/Smart Sync docs.

**Signals, each normalized to 0..1 — five of six already computed offline:**

| Signal | Source | Status |
|---|---|---|
| `coherence_n` — grid-fit residual (**PRIMARY**, best predictor of a constant grid) | `GridRefiner.Coherence` | **computed then DISCARDED — stop discarding; persist on `BpmResult`→`MusicTrack`** |
| `stable_n` — **constant-tempo proof**: first-half vs second-half (windowed) BPM delta, Gaussian on \|Δ\| (σ≈0.3 BPM) | — | **the ONE signal to ADD** (cheap: reuse `TempoEstimator` on two windows) |
| `acf_n` — tempo-ACF peak dominance (main ÷ runner-up); doubles as ½×/2× detector | `TempoEstimator` ACF | expose the dominance ratio, not just the winning BPM |
| `downbeat_n` — downbeat/meter salience | `DownbeatEstimate` dominance (floor 0.5) | feed as confidence; make `PhaseAlignToLeader`'s `Downbeat > 0` a *confidence*, not a boolean |
| `agree_n` — 2-member mutual agreement: local kick BPM vs online cross-check, half/double-aware | getsongbpm cross-check + `KickOnsets` | agreement raises, disagreement caps |
| provenance override | user-edited / imported grid | **hard override ⇒ 1.0** (Traktor grid-lock parity) |

**Fusion (weakest-link for the gate, product for display):**
- **Display** confidence = weighted **product** (weight `coherence_n` + `stable_n` highest) — intuitive, monotone.
- **Offer decision** = `min(coherence_n, stable_n, agree_n) ≥ 0.6` **AND** `downbeat_n ≥ 0.5`
  ⇒ offer beat/phase sync; else **downgrade to Tempo Sync**; provenance override forces offer.
- **Why `min`, not average:** a confident phase-lock genuinely needs *all* of {good fit, stable
  tempo, estimators agree}; an average lets one strong signal mask a fatal weak one.
- **Why 0.6:** the calibrated equivalent of the Essentia/Zapata "good ≈ 1.5-bit" line — the
  lowest defensible floor — and on the `min` it stays conservative (asymmetric toward
  downgrade). **Calibrate against a small labeled corpus; if in doubt, raise it.**

> Net effect: the gate is mostly **stop discarding + calibrate**, not new DSP — 5 of 6 inputs
> already exist. The only new computation is the constant-tempo `stable_n` window comparison.

## 8. Phase-correction policy (the musical part)

Golden rule: **a deck already audible in the mix must never get an aggressive seek** — a
hard jump is an audible skip, the #1 way sync "sounds broken." Three tiers by error size,
already implemented in `PhaseLockController` with these defaults (`PhaseLockSettings`):

| Tier | Condition (|phase error|) | Action | State |
|---|---|---|---|
| Lock zone (enter) | `< 0.02 beats` (~9.4 ms @128) | hold matched rate exactly | `Locked` |
| Lock zone (exit / hysteresis) | hold `Locked` out to `0.04 beats` | dead-band stops `Locked↔Active` chatter | `Locked` |
| Ride-in | between exit tol and re-snap | rate += error × gain `0.01`, clamped **±0.03** | `Active` |
| Re-snap | `> 0.25 beats` (¼ beat) | one-shot seek to nearest aligned beat (+ micro-correction that tick, no gap) | `Drifting` |

Refinements to add:
- **Playing follower re-snaps at most ±½ beat** (beat-level), never a bar jump — already
  enforced (`PhaseAlignToLeader` restricts bar-snap to non-playing decks). Preserve it.
- **Glide on master tempo change** — ramp the follower's matched rate over ~1 bar rather
  than stepping it (§5 transition).
- The **±0.03 (3%) clamp is a catch-up *ceiling*, not a steady-state value** — steady-state
  correction sits far below 1% and is inaudible. Fix the stale "sub-percent" wording in
  `PhaseLockSettings` (it describes steady-state, but reads as if 0.03 were sub-percent).

## 9. Half / double tempo handling

**Decision (built):** `TempoSyncCalculator.RateFor` folds to the nearest tempo octave at the
√2 boundary. 70→140 folds to ≈1.0× (beats align every other master beat), not a doubling.
Keep exactly.

**Indication (gap):** surface the active fold. When the applied relationship is `2×`/`½×` of
the master's displayed BPM, show the badge on the follower's sync/BPM readout so the DJ
understands why "70" is locked to "140." The `OctaveLabel` infra exists (`BpmMatch`) — wire
it to the **active sync fold**, not only to the matched-highlight.

**Stability:** near the √2 boundary (e.g. 96 vs 128) the fold can flip — add **hysteresis on
the fold ratio** (mirror the lock-zone dead-band) so the badge doesn't flicker, and allow a
**manual half/double override per deck** (rekordbox-style) for cases the DJ knows better.
The manual grid-BPM half/double (`DeckSetGridBpm` × 0.5/2.0) already exists — reuse it.

## 10. Key-lock interaction

- **Any tempo-changing sync auto-engages key lock** *before* applying the rate (so the rate
  takes the time-stretch path, not vinyl pitch). Already correct — keep, and keep the KEY
  LOCK button lit so the deck never lies about its state.
- **Sync may stretch beyond the manual pitch fader** (sync ceiling vs ±8% fader) precisely
  *because* key lock protects pitch. Document the asymmetry. Ceiling **decided at ±15%**
  (§12-2, down from ±35%): beyond it — and not a clean half/double — the deck reports
  `OutOfRange` rather than locking at a musically extreme stretch.
- **Micro-corrections stay sub-percent under key lock** → phase-holding never wobbles pitch.
- **DJ disables key lock while synced** → honor it (some want pitch to move as an effect) but
  warn that large stretches now shift pitch; never silently re-enable.
- **CPU pressure:** key-locked stretch is heavier than vinyl. Degrade the *correction
  cadence* (fewer/gentler corrections), **never the audio** — "drop corrections, not audio"
  is the supreme rule.

## 11. UI feedback requirements

The DJ must read the whole sync state at a glance, in a dark club. All push, not poll
(`SyncStateChanged` already fires outside the audio gate).

1. **Master vs follower**, per deck — a MASTER badge; followers subordinate. Pinned master
   visually distinct from auto/soft master.
2. **Active mode** — Tempo Sync vs Beat Sync vs Sync Lock must look different; a latch must
   not look like a one-shot.
3. **Lock state** — render all five, colour-blind-safe: `Off` neutral · `Active` amber
   ("pulling in") · `Locked` green ("in the pocket") · `Drifting` flashing amber
   ("recovering") · `OutOfRange` red ("can't sync"). Today only `OutOfRange` is shown.
4. **Grid quality** — solid / "grid uncertain" / "not analyzed" (§7).
5. **Half/double** — the `2×`/`½×` badge (§9).
6. **Sync impossible** — `OutOfRange` red with the reason; the button must **say** it (today
   only logged).
7. **Phase meter (optional, pro)** — a small leader-vs-follower offset meter reading
   `BeatPhaseError` directly; cheap, high trust value.

## 12. Deliberate decisions — DECIDED (owner, 2026-07-17)

The five product-disagreement calls are decided; #6 is under focused research.

1. **Default SYNC button = continuous Sync Lock (latch).** ✅ The big SYNC button engages the
   continuous lock. Tempo Sync and one-shot Beat Sync are exposed as **alternates** (mode
   selector / secondary), not the primary button. *(Matches Mixxx / Serato Smart.)*
2. **Sync stretch ceiling tightened to ±15%.** ✅ Down from ±35%. Beyond ±15% (and not a clean
   half/double) the deck reports `OutOfRange` / "can't sync" instead of locking at a musically
   extreme stretch. `SyncRangePercent` → `0.15`. *(Tunable; leaned to the tight end per owner.)*
3. **Pitch-bend on a Sync-Locked deck is FORBIDDEN — locked means locked.** ✅ A locked deck
   stays glued to the master; manual bend does not fight the loop. Keep the current `PitchBend`
   no-op while `SyncLocked`. *(Traktor-style hard lock.)* To bend by hand, drop to Tempo Sync.
4. **Resnap on a playing deck = beat-level only.** ✅ A playing follower re-snaps at most ±½
   beat (never a bar jump); bar-level snap stays pre-fade only. Matches the existing
   `PhaseAlignToLeader` predicate — keep it.
5. **Master model = soft default + manual pin + Ableton Link as first-class master.** ✅
   (Owner deferred to recommendation.) Auto/soft leader as today, an explicit MASTER pin, and
   the shared beat clock / Link able to be the master — lean into the shared-clock advantage.
6. **Grid-confidence gate — DECIDED (research-backed).** ✅ Approach = **committee/mutual-
   agreement + a constant-tempo proof**, fused **weakest-link `min` for the gate** (product for
   display), conservative floor **0.6** (calibrated to the Essentia/Zapata "good ≈ 1.5-bit"
   line), **biased toward downgrade**. Full model + signal set + citations in §7. We already
   have 5 of 6 inputs; the work is **stop discarding `GridRefiner.Coherence`** + **add one
   constant-tempo `stable_n` signal**. Calibrate the 0.6 on a small labeled corpus.

## 13. Acceptance tests (black-box, musical)

Pass/fail the way a pro DJ judges — by ear and by watching the grid. "No audible drift" =
beats stay flammed `< ~10 ms` for the duration. These are the regression corpus; each maps
to a Core unit test (pure math) and/or a runtime check.

**Tempo Sync**
1. Load 128 + 124; Tempo Sync B→A. B reads 128 BPM, pitch unchanged (key lock), **phase NOT
   moved** — a deliberately off-beat B stays off-beat.
2. Tempo Sync engaged, both playing; nudge B's jog/pitch-bend → bends freely (tempo sync
   does not fight manual phase).
3. Tempo Sync, then move master pitch +2% → B tracks the new tempo continuously; still no
   phase snap.

**Beat Sync (one-shot)**
4. Two 128 tracks ~1 beat out of phase; Beat Sync B→A once → B's downbeat snaps onto A's
   grid in one move, no ongoing pitch wobble afterward.
5. Downbeat known → aligns to the **bar** (the "one" matches), not just nearest beat.
6. Only beat-level grid (no downbeat) → aligns to nearest beat; UI does **not** claim bar
   alignment.
7. After a one-shot Beat Sync, scratch B → B does **not** auto-return (distinguishes it from
   Sync Lock).

**Sync Lock (continuous)**
8. Two 128 tracks; Sync Lock B→A; run **4 minutes** → phase-locked throughout, no audible
   drift, no pitch pumping. *(The headline test today's approach fails.)*
9. Sync Lock; scratch/hold B's jog 2 bars, release → recovers to phase within ~1–2 bars, no
   click.
10. Sync Lock; beat-jump B +4 beats → lands back in phase, bar preserved, no sustained drift.
11. Sync Lock; 4-beat loop on B, exit → re-locked in phase on exit.
12. Sync Lock; move master pitch ±3% slowly → B follows continuously, stays locked, pitch of
    both preserved.
13. Two decks Sync-Locked at 128 over 8 minutes → beats never visibly separate on the stacked
    waveforms.

**Master election**
14. A master (playing), B synced; **A ends** → B keeps playing at current tempo (no freeze/
    jump); sync releases or re-elects, never locks to stopped A.
15. A master, B synced; **unload A** → B keeps playing steadily; master badge clears; SYNC on
    B doesn't wedge.
16. Both stopped; Play A → A auto-master; Play B with SYNC armed → B enters synced to A.
17. **Pin** B master while A plays → A follows B; start/stop A does not steal master; unpin →
    auto election resumes.
18. Master A **paused** (not ended) → B must **not** lock to A's frozen grid; holds own tempo
    until A resumes or another master is valid.

**Armed / quantized start**
19. B stopped; SYNC → B's BPM immediately reads the master tempo (button not dead), Active/
    armed. Play → B starts **in phase** (Quantize on: next bar; off: pulled in). No "sync did
    nothing."
20. Quantize = Bar → armed deck started mid-bar begins on the next downbeat, "one" aligned.
21. Quantize = Beat → same deck starts on the next beat (tighter).

**Half / double**
22. Sync 70→140 → follower ≈1.0× (not doubled/chipmunk), beats align every other master beat,
    `2×`/`½×` indicator shows.
23. Sync 85→170 → folded, indicated, and the fold decision is **stable** (no flicker) if the
    master BPM wobbles near the boundary.
24. Manual half/double override on an ambiguous pair (96 vs 128) flips the interpretation; the
    indicator follows.

**Grid gate, range, key lock**
25. Sync using a **low-confidence / unanalyzed** track → phase sync **refused/downgraded to
    tempo-only**, UI shows "grid uncertain," no confident mis-align. Re-analyze to high
    confidence → full Beat Sync becomes available.
26. Sync 100→150 (beyond ceiling, not a clean half/double) → SYNC shows **OutOfRange /
    "can't sync"** with reason; deck holds own tempo (no extreme stretch).
27. Sync needing +6% → pitch does **not** rise (key lock honored); disable key lock manually
    → pitch shifts with tempo and a warning shows.

## 14. Proposed phased plan (build after §12 is approved)

Ordered by leverage-per-risk; each phase is independently shippable and testable, and none
touches the audio-thread hot path except where noted.

- **P0 — Grid-confidence gate (§7). ✅ IMPLEMENTED 2026-07-17** (Core + Audio suites green; App
  tests written, pending an app-closed build). (a) `BpmResult.GridCoherence` — stop discarding
  `GridRefiner.Coherence`; (b) new constant-tempo `BpmResult.TempoStabilityBpmDelta` (half-vs-half
  BPM delta); (c) pure `GridConfidenceCalculator` (`min` gate / product display, floor 0.6,
  null=Unknown=preserve); (d) engine gate — `DeckSlot.PhaseSyncReady` + `DeckSetPhaseSyncReady`
  action; `PhaseAlignToLeader` + `CorrectSlaveLocked` downgrade to tempo-only when uncertain
  (bar-level still gates on the downbeat, so four-on-the-floor still phase-locks); (e)
  `DeckViewModel.DispatchGridConfidence` pushes the gate on load + the feedback channel carries
  "grid uncertain". Analyzer **v9** (background re-analyze). A full visual indicator is P2.
- **P1 — Mode taxonomy (§4).** Add Tempo Sync and a non-latching Beat Sync alongside the
  existing Sync Lock; one new `PerformanceActionKind` per mode (or a mode parameter);
  Settings default (§12-1). Reuses the existing engine.
- **P2 — UI feedback (§11).** Render all five lock states, master/follower badge, grid
  quality, and wire the `2×`/`½×` fold badge (§9). Push-only.
- **P3 — Armed / quantized start (§6).** SYNC arms on a stopped deck; Play enters on the next
  beat/bar; Quantize becomes a persistent trigger modifier with a deck-UI button.
- **P4 — Master model (§5).** Manual MASTER pin + fall-back rules; write election as
  re-elect-first so it is 4-deck-correct.
- **P5 — External master / Ableton Link (§5).** Implement `BeatClockSource.External`; the
  shared clock drives both decks. Largest, sequenced last.
- **Tuning — §12-2 ceiling, §12-3 bend-while-locked, §8 glide + wording fix.** Fold into the
  phases they touch.

Every phase lands its slice of the §13 acceptance corpus as tests first (TDD): the pure-math
scenarios as Core xUnit, the runtime/UI scenarios as documented, reproducible checks.
