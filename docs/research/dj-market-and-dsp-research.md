# DJ Software Market & DSP Research — Input for the DJ-Engine Spec

> **Status:** Research input, 2026-06-02. Feeds `docs/03-beat-engine.md` and
> `docs/11-deck-ab-pro-dj.md`. Produced by a fan-out + adversarial-verification
> research pass (5 angles, 24 sources fetched, 108 claims extracted, 25 verified,
> 22 confirmed). **Not a spec** — a sourced evidence base to write the spec from.

## Confidence legend

- **[VERIFIED]** — survived 3-vote adversarial verification (≥2/3), backed by a
  primary source (Mixxx code/docs, peer-reviewed DSP papers, vendor manuals).
- **[DOMAIN]** — author/domain knowledge, **not** verified this round. Treat as a
  working assumption to confirm before locking spec language.
- **[REFUTED]** — a claim that was tested and failed; listed so we do **not** assert it.

---

## ⚠️ Scope honesty note

The verification round is **rich on HOW features work** (Focus Area 2) and **thin on
the cross-vendor feature matrix** (Focus Area 1). Most market-comparison sources were
blogs, not primary, so the matrix below is **[DOMAIN]** unless marked otherwise. The
technical sections lean heavily on **Mixxx** (the only major open-source product) — treat
Mixxx as a credible *reference design*, not an industry-wide guarantee, since
Serato / rekordbox / Traktor internals are closed and undocumented.

---

## PART 1 — Market Feature Mapping

Products surveyed: **Serato DJ Pro, Pioneer rekordbox, Native Instruments Traktor Pro,
VirtualDJ, Algoriddim djay Pro, Mixxx (open-source).**

### 1.1 Tiering (table-stakes vs nice-to-have vs differentiator)

| Tier | Features | Notes |
|------|----------|-------|
| **TABLE-STAKES** (must-have for credibility) | Beat sync (tempo **+** phase) · BPM/beat detection with beatgrids · Looping (manual + auto) · Hot cues / cue points · Tempo & pitch control with **keylock / master tempo** · Per-channel 3-band EQ + filter · FX units · Crossfader + channel faders · MIDI controller mapping · Library / track management · Recording | These define "this is a real DJ app." Missing any one reads as a toy. *[DOMAIN], partial [VERIFIED] on sync/beatgrid/keylock/EQ-filter mechanisms in Part 2.* |
| **EXPECTED / strongly desired** | Key detection + harmonic mixing (Camelot wheel) · Loop roll / beat-jump · 4 decks · Sampler · Streaming-service integration (Beatport/Tidal/SoundCloud etc.) · Quantize / snap-to-beat | Present in most pro tools; users notice the absence but it is not a hard credibility gate. *[DOMAIN]* |
| **DIFFERENTIATORS** | **Tight audio↔visual coupling — beat-synced visuals driven by the same action layer, controlled simultaneously** (Liveolator's core bet) · **Effortless / near-automatic beat sync + mix assist** that frees the performer's attention for the visuals · Autopilot on the DJ side as a first-class mechanism | Where Liveolator competes. **NOT** competing on stem separation or pro-DJ FX depth. *[Product direction, 2026-06-02]* |
| **EXPLICITLY DE-PRIORITIZED** | AI stem separation · 4 decks · pro-DJ FX-maximalism · multi-feature "kitchen-sink" DJ tooling | Out of scope for MVP (possibly out of scope entirely). Liveolator stays **2 decks**. *[Product direction, 2026-06-02]* |

### 1.2 Per-product positioning (high level) — *[DOMAIN]*

- **Serato DJ Pro** — industry standard for controllers/club; strong stems; large hardware ecosystem.
- **Pioneer rekordbox** — tied to Pioneer CDJ/club hardware; library prep workflow; "Export" vs "Performance" modes.
- **Traktor Pro (NI)** — deep FX, remix decks, respected sync/loop engine; strong with NI/Maschine ecosystem.
- **VirtualDJ** — very broad feature set, aggressive stems, video support, large casual/mobile-DJ base.
- **djay Pro (Algoriddim)** — Apple-ecosystem polish, Neural Mix stems, approachable UI, strong macOS/iOS.
- **Mixxx** — open-source, cross-platform, full DJ feature set; **the only product whose exact mechanisms we can cite** (see Part 2).

### 1.3 Product direction (2026-06-02) — the differentiator

**Liveolator is deliberately NOT a maximalist pro-DJ tool.** Its uniqueness is the
**tight coupling between visuals and music, and controlling both at once.** The DJ engine
exists to make **beat sync and mixing effortless — near-automatic — so the performer's
hands and attention are freed to play the visuals.**

Concrete consequences for the DJ-engine spec:

1. **Beat sync must "just work"** — one-button / automatic, with octave-error (½×/2×) and
   phase locking handled transparently. The performer should never *babysit* the sync.
2. **Mix assist is a feature, not an afterthought** — auto-tempo-match, and likely
   transition/crossfade assist, so deck-to-deck moves don't demand continuous attention.
3. **The `PerformanceAction` layer is the heart of the differentiator** — one action
   beat-syncs *both* audio *and* visuals. This is what makes "control both simultaneously"
   real, and it is the seam every input (Push 1, CMD STUDIO 2A, UI, autopilot) feeds.
4. **Autopilot (`docs/10`) on the DJ side is a core mechanism, not a side feature** — it is
   what frees the operator to focus on the visual performance.
5. **Explicitly de-prioritized:** stem separation, 4 decks, pro-FX maximalism. Liveolator
   stays **2 decks** and invests effort where it differentiates.

- The TABLE-STAKES row remains the **MVP credibility bar** — but executed for *ease*, not
  depth. The seam architecture (`PerformanceAction` → dispatcher → engines) must express
  all table-stakes features as serializable actions.
- **Open question (narrowed):** decide MVP stance on **key detection / harmonic mixing**
  (it *supports* effortless mixing, so it may earn its place) — while 4 decks, sampler,
  and stems are de-prioritized per the direction above.

---

## PART 2 — How the Critical Features Work (Technical)

### 2.1 BPM / tempo detection — three-stage pipeline **[VERIFIED, high]**

Adopt the standard three-stage architecture:

1. **Onset detection** — compute a detection function from **spectral flux** (log-power
   flux summing only the *positively-changing* frequency bins of a decimated STFT), then
   low-pass filter it (e.g. Percival/Tzanetakis: 1024-sample frames, 128 hop ≈ 344.5 Hz,
   14th-order FIR ~7 Hz LPF).
2. **Periodicity / tempo estimation** — **autocorrelation** of the onset-strength signal
   (and/or a spectral-product method) to find the dominant period.
3. **Beat-location / phase estimation** — **cross-correlate** the detection function with
   an artificial **pulse train** at the estimated tempo (a comb-filter approach); the max
   is the first beat, subsequent beats placed one period apart via local peak search.

Sources: Alonso/David/Richard ISMIR 2004; Percival/Tzanetakis TASLP 2014; Mixxx ships the
Queen Mary Vamp plugin implementing exactly this. *(Vote: unanimous on architecture; 2-1
on the spectral-product-vs-autocorrelation ranking detail.)*

### 2.2 Octave errors are expected **[VERIFIED, high]**

Estimating ½×, 2×, ⅓×, 3× the true tempo is a **fundamental, common** failure mode
(mirrors human tapping ambiguity). The field evaluates with **Accuracy 1** (within 4% of
ground truth) and **Accuracy 2** (within 4% of a ⅓/½/1/2/3 multiple); Acc2 typically
beats Acc1 by ~20 points. **The engine must handle this** — e.g. the halve/double factor
in sync (see 2.4). Source: Gouyon/Klapuri-style TASLP comparison (MIREX standard).

### 2.3 Beatgrid construction **[VERIFIED, high]**

- **Default = constant/fixed-tempo grid:** a single **beat offset (in frames/samples) +
  one BPM** projects an equidistant grid across the whole track.
- Justified by perceptual tolerance: beats within **~±25 ms** still sound aligned; tempo
  changes of **~±0.03%** are imperceptible. *(±25 ms / ±0.03% detail: 2-1.)*
- **Variable-tempo fallback:** present the raw analyzer beatgrid for tracks whose tempo
  drifts (Mixxx "Assume constant tempo" toggle).
- Source: Mixxx wiki (`beatgrid.h`), Mixxx manual beat-detection prefs.

### 2.4 Beat sync — leader/follower model **[VERIFIED, high]**

- **Leader/follower (formerly master/slave).** Mixxx Sync Lock modes: **None, Follower,
  LeaderSoft** (auto-selected, reassignable), **LeaderExplicit** (user-set, sticky, always
  wins), with an **internal clock** as fallback.
- **Tempo matching:** followers set playback rate via
  `required_rate = (leader_bpm / deck_base_bpm) * halve_double_factor`, propagating BPM
  changes to all syncables except the source. The halve/double factor lets a 70 BPM track
  follow a 140 BPM leader without an out-of-range pitch shift (ties to 2.2).
- Source: Mixxx `src/engine/sync/syncable.h`, Master-Sync dev wiki.

### 2.5 Phase locking **[VERIFIED, high]**

- Uses **beat distance** — a fractional value in `[0.0, 1.0)` measuring playhead progress
  through the current beat.
- **Phase error = shortest circular percentage change** between target and current beat
  distance (the beat treated as a phase circle). Source: Mixxx `bpmcontrol.cpp`
  (`BpmControl::calcSyncAdjustment` → `shortestPercentageChange`); `enginesync.cpp`
  propagates `beatDistance`.

### 2.6 Keylock / master tempo & time-stretch/pitch-shift **[VERIFIED, high]**

- **Plain resampling changes time AND pitch together** — independent control needs a
  separate time/pitch-scaling algorithm.
- Two canonical families:
  - **Time-domain SOLA / WSOLA** — cut audio into ~tens-to-hundreds-of-ms segments,
    skip/repeat to change duration without changing pitch. **Pitch shift = SOLA
    time-stretch + resample back to original duration.**
  - **Frequency-domain phase vocoder** — historically for time-scaling, adapted for
    real-time pitch shifting (Laroche & Dolson JAES 1999).
  - **Hybrid PVSOLA** (Moinet & Dutoit DAFx-11) — periodically resets phase-vocoder
    phases with an unmodified input frame (position found by cross-correlation).
- **Product lesson:** let users pick the keylock engine by performance profile. Mixxx
  offers **SoundTouch ("faster")** for low-power machines / when buffer underflows occur
  during keylock — tradeoff: worse on large tempo changes — alongside higher-quality engines.
- Sources: SoundTouch/Surina; Laroche & Dolson JAES 1999; Moinet & Dutoit DAFx-11; Mixxx 2.5 manual.

### 2.7 Low-latency audio fundamentals **[VERIFIED, high]**

- Latency is governed by the **audio buffer size** = the amount of audio (in ms) processed
  per callback. It directly sets the delay between a control action (crossfader move, play
  press) and the audible result; **smaller buffer → lower latency but higher underflow
  risk.** This is the core tunable for Liveolator's "low-latency audio" requirement.
- Note the coupling: heavier keylock engines raise underflow risk at a given buffer size
  (2.6 ↔ 2.7). Source: Mixxx 2.5 manual + Adjusting-Audio-Latency wiki.

### 2.8 Key detection & harmonic mixing — **[DOMAIN, NOT verified this round]**

Requested but **not** substantiated by verified claims. Working description to confirm
later: musical key via **chromagram / pitch-class profiles** (12-bin energy per pitch
class) matched against key templates; **harmonic mixing** organizes keys on the
**Camelot wheel** so adjacent/relative keys mix cleanly. **Must be sourced separately**
before it enters the spec.

---

## REFUTED — do **not** assert these

- ❌ Mixxx adaptive beatgrid supports arbitrary intra-track tempo via multiple coordinate
  sets *(1-2)*. → Do not assume mature arbitrary-tempo grids.
- ❌ Mixxx downbeat detection via DFT×ACF tempogram multiplication *(1-2)*.
- ❌ Laroche/Dolson phase-vocoder techniques give a real-time/low-latency **compute**
  advantage *(1-2)*. → Do not claim a latency advantage for the phase vocoder.

---

## Open questions carried into the spec

1. **Cross-vendor feature matrix** (Serato/rekordbox/Traktor/VirtualDJ/djay) — needs a
   dedicated vendor-spec-page sourcing pass; this round did not verify it.
2. **Closed-engine divergence** — do Serato/rekordbox/Traktor diverge from Mixxx's
   leader/follower + constant-grid + SOLA/phase-vocoder reference model? (Unknowable from
   public sources — design to the Mixxx reference.)
3. **Key detection / Camelot algorithmics** — explicitly requested, still unsourced.
4. **Default buffer-size / latency targets** for Windows vs macOS — what should Liveolator
   ship by default given the low-latency + cross-platform requirement?
5. **MVP feature line** — confirm which "expected" features (key/harmonic, 4 decks,
   sampler, streaming, stems) are in vs out of the first DJ-engine release.

---

## Source quality (selected)

**Primary / authoritative:** Mixxx code & manual (`beatgrid.h`, `bpmcontrol.cpp`,
`syncable.h`, Master-Sync wiki, beat-detection & sound-hardware prefs); ISMIR 2004
(Alonso/David/Richard); TASLP 2014 (Percival/Tzanetakis); JAES 1999 (Laroche & Dolson);
DAFx-11 PVSOLA (Moinet & Dutoit); SoundTouch/Surina; Mixed In Key (Camelot).
**Blog / secondary (Part 1 market mapping):** recordcase, dj.studio, digitaldjtips,
internettattoo — used for orientation only, **not** verified.
