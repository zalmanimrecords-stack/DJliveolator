# Gap-Closure Research — Key Detection · Latency · Auto-Mix · Audio↔Visual Sync

> **Status:** Research input, 2026-06-03. Companion to
> `docs/research/dj-market-and-dsp-research.md`. Closes the four gaps that the first
> round left open, framed for the product direction (effortless sync/mix + tight
> audio↔visual coupling, 2 decks, no stems).
>
> **Provenance / honesty note:** This round's workflow completed search → fetch →
> claim-extraction → adversarial verification, but the **final synthesis agent crashed**.
> This document was reconstructed by the main agent directly from the run's journal:
> **203 unique claims** were extracted from 24 sources; the verification corpus cast
> **122 votes with only 6 refutations (~95% pass)**, so the surviving claims are solid.
> Confidence is **[VERIFIED]** where a claim carried a primary source and survived
> verification; **[DOMAIN]** where it is author inference connecting verified facts.

---

## 🔑 Headline finding — the audio↔visual differentiator has a proven model: a shared beat clock (Ableton Link)

The single most important result for Liveolator: the "control visuals and music together,
locked to the same beat" idea is a **solved pattern**, realized by **Ableton Link** — an
open, peer-to-peer clock-sharing protocol that synchronizes **tempo, beat, phase, and
start/stop** across separate audio and visual apps, and which **Resolume already uses** to
beat-lock visuals to DJ software. Liveolator should adopt the *same internal architecture*
— one shared beat timeline that drives BOTH the DJ mix and the visual animation — and can
optionally interoperate with Ableton Link externally. Details in **Gap 4**.

---

## GAP 1 — Musical key detection & harmonic mixing

### 1.1 Key detection is template-based (pitch-chroma vs key profiles) **[VERIFIED]**

- The dominant method is **template-based**: extract a **12-dimensional pitch chroma /
  pitch-class profile (PCP)** — an octave-independent vector of the energy in each of the
  12 semitone classes (computed by folding the spectrum into semitone bands and summing
  octave-spaced bands) — then **compare it to a template key profile** for each of the 24
  major/minor keys and pick the most similar. *(claims 64, 66, 53)*
- Key profiles/templates are derived four established ways: **human tonality perception
  (Krumhansl)**, **diatonic models**, **extraction from MIDI/symbolic data (Temperley)**,
  and **extraction from audio**. *(claim 65)*

### 1.2 The two canonical key-finding algorithms **[VERIFIED]**

- **Krumhansl–Schmuckler:** build a 12-value input vector of each pitch class's total
  duration; correlate it against each of the 24 key-profile vectors; the **highest
  correlation** names the key. Profiles come from Krumhansl–Kessler (1982) probe-tone
  experiments (tonic rated highest, then triad, then scale, then chromatic notes).
  *(claims 47, 81, 82)*
- **Temperley probabilistic (Bayesian):** treat the key profile as a probability over
  scale degrees; `P(melody|key)` = product of per-note profile values; pick the key
  maximizing it (equal priors). On synthetic data it matched **100%** vs K-S **81.7%**.
  *(claims 48, 49)*

### 1.3 How Mixxx implements it (reference design) **[VERIFIED]**

- **Default/stable analyzer = QM-DSP library** key detector. *(claim 100)*
- **Optional: KeyFinder / libkeyfinder** (Ibrahim Sha'ath), integrated **as of Mixxx 2.3**;
  the Mixxx team took over libkeyfinder maintenance after a 2020 handover. *(claims 9, 16, 96, 99)*
- Mixxx's GSoC 2012 key work evaluated **three** engines: Queen Mary Vamp key detector,
  libKeyFinder, and CLAM's tonal-analysis object. *(claim 63)*

### 1.4 Camelot wheel & harmonic-mixing rules **[VERIFIED]**

- **Encoding:** each key = **number 1–12 + letter**, arranged like a clock face.
  **B = major (outer ring), A = minor (inner ring).** E.g. **8B = C Major, 8A = A Minor.**
  *(claims 32, 35, 57, 60)*
- **Compatible moves:**
  - **Adjacent:** ±1 number, **same letter** (8A → 9A or 7A) → smooth, subtle energy shift/boost. *(claims 33, 37, 59, 61)*
  - **Relative major/minor:** **same number, switch letter** (8A ↔ 8B). *(claims 34, 36, 58, 62)*

> **Spec implication (serves "easy to mix"):** key detection + a Camelot-rule "what mixes
> next" hint is cheap to implement (PCP + template match offline at analysis time) and
> directly lowers the skill needed to mix well — consistent with the product direction, so
> it earns a place in scope. The harmonic-compatibility rules are trivial table lookups.

---

## GAP 2 — Low-latency audio targets & the real-time callback model

### 2.1 The callback model & its hard deadline **[VERIFIED]**

- The OS audio API requests a buffer of samples **hundreds of times per second** on a
  **realtime, performance-sensitive callback thread**. *(claims 94, 10)*
- **Buffer size (samples) = latency_ms × samplerate × 2 (stereo)**; the OS requests a
  buffer every `latency_ms` (1 ms latency → 1000 requests/s). *(claim 11)*
- **Hard per-buffer deadline = the buffer period.** 256 samples @ 44.1 kHz ⇒ each buffer
  must be computed in **< 5.8 ms**, every time, no exceptions. *(claim 91)*

### 2.2 What must NEVER happen on the audio thread **[VERIFIED]**

- **No** memory allocation/free (`malloc`/`free`/`new`/`delete`), **no** locks/mutexes,
  **no** blocking on semaphores/disk/network, **no** calls into OS/3rd-party code that may
  block internally, **no** unbounded-time operations. Blocking the callback causes audible
  **xruns / buffer under-runs**. *(claims 10, 42, 44, 93, 95)*
- Cross-thread communication (GUI/MIDI ↔ audio) must be **lock-free**: atomics, flags,
  **ring buffers**, queues. *(claim 43)*
- **Architectural rule for Liveolator:** the `PerformanceAction` dispatch into the audio
  engine must hand off to the RT thread via a lock-free queue; never mutate audio state
  under a lock. **[DOMAIN]**

### 2.3 Concrete buffer/latency targets **[VERIFIED]**

- **Mixxx guidance:** **23–64 ms** buffer is acceptable for keyboard/mouse or controller
  use; **< 10 ms** when timecode vinyl is used; a 23 ms buffer ⇒ ~23 ms before audio
  responds. *(claims 29, 70, 71)*
- **Interactive/DJ target (Bencina):** ~5 ms is already a "large" buffer; ~1 ms (64 samples
  @ 44.1 kHz) is good; a reasonable **end-to-end** target for interactive systems is
  **< 8 ms** including all system latency. *(claim 92)*
- **Tradeoff:** smaller buffer → lower latency but more CPU + higher glitch risk; set it as
  small as the system reliably handles. *(claims 22, 30, 69, 72)*
- **Floor:** latency cannot go below one callback buffer period under normal operation. *(claim 68)*

### 2.4 Platform differences (Windows vs macOS) **[VERIFIED]**

- **API quality:** Windows **ASIO** (bypasses kernel mixer) and **WDM-KS** = *Good*;
  **WASAPI** = *Acceptable*; **DirectSound/MME** = *Poor*. **macOS = CoreAudio only**.
  Linux ALSA/JACK = *Good*. *(claim 31)*
- **Windows 10+** defaults to a **10 ms** buffer for all apps; small buffers require opting
  in via **AudioGraph** or **WASAPI `IAudioClient3`**; updated HDAudio driver supports
  **128–480 samples (2.66–10 ms @ 48 kHz)**; `IAudioClient3` exposes a periodicity model
  (legal periods = multiples of `fundamentalPeriodInFrames`). *(claims 75, 76)*
- **macOS CoreAudio** lets the callback run for the whole period → behaves like a clean
  **double-buffer**. *(claim 25)*
- **Tuning differs per OS:** Windows = match ASIO sample rate + sound-card IRQ priority;
  macOS = raise process priority (`renice -20`). *(claim 23)*
- **Total latency = sum of stacked buffer layers** (driver + API + app buffers), so layer
  count matters as much as buffer size. *(claims 73, 74)*
- **PortAudio** exposes `defaultLowLatency` (interactive) and `defaultHighLatency` (safe)
  per device — a sensible source for default values. *(claim 67)*

> **Spec implication:** target **< 10 ms** output latency on both platforms; default buffer
> ~**256–512 samples** and expose it as a user setting (Mixxx-style). Prefer ASIO/WDM-KS on
> Windows and CoreAudio on macOS through the `IAudioSource` seam. Keep the audio thread
> allocation-free and lock-free. **[DOMAIN, grounded in the verified claims above]**

---

## GAP 3 — Effortless / automatic beat sync & mix assist

### 3.1 One-button sync (the Mixxx Sync Lock reference) **[VERIFIED]**

- **Sync Lock = one button**: matches **tempo** across all Sync-lit decks (peer model — all
  synced decks are equal; changing any one's rate changes the others), and **auto-handles
  double/half BPM** relationships (75 BPM follows 150 BPM cleanly). *(claims 1, 77, 13)*
- **Leader selection** via `pickLeader()` priority: **explicit user leader > a playing/audible
  deck > internal clock** fallback. *(claim 12)*
- **Rate formula:** `required_rate = (leader_bpm / deck_base_bpm) × halve_double_factor`
  (×0.5 / ×2 multiplier). *(claim 13)*

### 3.2 Phase alignment is a SEPARATE switch: Quantize **[VERIFIED]**

- **Tempo sync ≠ beat alignment.** Sync Lock matches tempo only; **Quantize** snaps beats
  into perfect phase alignment. Phase correction runs **only when Quantize is on**; off, the
  engine returns the raw rate. *(claims 2, 15, 78)*
- Phase uses a **beat-distance value `[0.0, 1.0)`** (0.0 = on a beat, 0.5 = half-beat away);
  proportional rate corrections **capped at ±5%** for errors in `[0.01, 0.2)`. *(claim 14)*

### 3.3 Automatic mixing / auto-transition (Mixxx Auto DJ) **[VERIFIED]**

- Auto DJ **drives the crossfader exclusively**; needs ≥1 deck on each crossfader side. *(claim 3)*
- Uses **four intro/outro cue markers** (Intro Start/End, Outro Start/End); **Intro Start &
  Outro End auto-set by silence detection**. *(claims 17, 107)*
- **Default "Full Intro + Outro" mode:** crossfade duration = the **shorter** of (outgoing
  outro, incoming intro); when the outro is longer, the next track starts during the outro
  to align intro/outro ends. *(claims 4, 79)*
- Transitions are **clamped to musical boundaries** (end at Intro End or Outro End,
  whichever first). *(claim 18)*
- **Auto tempo-match** kicks in when the two BPMs are **within 6%**. *(claim 7)*
- Transition **style chosen automatically** from the two BPMs + configured transition time.
  *(claim 6)*
- **Other modes:** Full Track / Skip Silence ignore cues and crossfade over a fixed number
  of seconds; CD / Radio-Jukebox / DJing modes differ in load & transition points. *(claims 80, 108)*

### 3.4 Smarter cue/transition placement (academic) **[VERIFIED]**

- *Automatic Detection of Cue Points for the Emulation of DJ Mixing* (Computer Music
  Journal, MIT Press): detects **"switch points"** — cue points for **beat-/downbeat-/
  phrase-aligned** automatic transitions in EDM — via **feature extraction + novelty
  analysis** grounded in **rules elicited from interviews with professional DJs** (a hybrid
  expert-rule + structural-novelty approach, not end-to-end ML). *(claims 19, 26, 27, 83, 104, 105)*
- Reliability: conference version **~96%** usable switch points; journal version **~90%** on
  unseen tracks — i.e. automatic cue placement is viable for an effortless auto-mix engine.
  *(claims 20, 28, 84, 106)*

> **Spec implication:** the differentiator ("free the hands for visuals") maps to a
> **layered automation ladder**: (1) one-button **Sync Lock** + (2) **Quantize** for phase +
> (3) an **Auto-Mix** mode that uses silence-detected intro/outro cues and phrase-aligned
> switch points to crossfade hands-free. Each layer is an opt-in `PerformanceAction`. This
> is also the natural hook into Autopilot (`docs/10`). **[DOMAIN, grounded above]**

---

## GAP 4 — Audio→visual beat-sync coupling (the core differentiator)

### 4.1 Ableton Link = the shared-beat-clock pattern **[VERIFIED]**

- **Link synchronizes tempo, beat, phase, and start/stop** across multiple apps on one or
  more devices, **peer-to-peer with no master/slave**; any peer can change tempo and all
  follow; peers join/leave without interrupting the session. *(claims 50, 55, 102)*
- **Decentralized tempo:** any participant proposes a tempo at any time; each adopts the
  **last proposed tempo seen**; the session converges quickly. *(claims 38, 87)*

### 4.2 The Link timeline & quantum (the actual API model) **[VERIFIED]**

- Shared timing = a **timeline tuple `(host time, beat time, tempo)`** with the linear
  relation **`BeatTime / HostTime = Tempo`** (a bijection between beat and time). A tempo
  change creates a new timeline crossing the chosen host-time point and is broadcast.
  *(claims 51, 90, 97)*
- **Quantum** = the **phase-sync unit in beats**; clients using the **same quantum are
  phase-aligned**, and alignment composes (start of an 8-beat loop always coincides with
  start of a 4-beat loop). Phase/beat for a host time come from
  **`phaseAtTime(hostTime, quantum)`** and **`beatAtTime(hostTime, quantum)`**. *(claims 40, 52, 89, 98)*
- **Beat alignment is integral:** an integer beat on one peer maps to an integer beat on all
  others (integer offsets allowed, never beat 3.5). *(claims 39, 88)*
- **Quantized launch:** transport start **waits for the next quantum boundary** so multiple
  devices start exactly together on the bar grid. *(claim 41)*

### 4.3 Resolume proves the audio↔visual case **[VERIFIED]**

- Resolume uses Link to keep **BPM + position-in-measure (beat phase)** synced across
  connected apps/computers; sync is **bidirectional** (change BPM in either, the other
  follows). *(claims 45, 46, 54, 85, 86, 101)*
- Once synced, Resolume's **BPM-synced effect animations stay locked to the music and
  auto-track tempo changes** — a single shared beat clock driving the visuals. *(claims 56, 103)*

> **Spec implication — this is the architectural heart of Liveolator:** the Core beat
> engine should expose a **Link-style internal beat timeline** `(hostTime, beatTime, tempo)`
> + a **quantum** for bar/phrase alignment. BOTH the DJ mix scheduler AND the visual
> compositor read phase/beat from this **one** clock, so "control both simultaneously" and
> "beat-synced visuals" fall out for free. Visual parameter changes and clip launches use
> **quantized launch** (snap to the next beat/bar) — the visual analogue of audio Quantize.
> Optionally, **interoperate with real Ableton Link** so Liveolator can sync to/from
> Ableton, Resolume, and other Link apps on stage. **[DOMAIN, directly grounded in 4.1–4.3]**

---

## Consolidated spec implications (carry into the engine docs)

| Area | Decision to record | Target doc |
|------|--------------------|-----------|
| Key detection | In scope; PCP + key-profile template match at analysis time; Camelot "mix-next" hints | `docs/03-beat-engine.md` (+ new key section) |
| Sync UX | Two distinct controls: **Sync Lock** (tempo, one-button, auto ½/2×) and **Quantize** (phase snap) | `docs/11-deck-ab-pro-dj.md` |
| Sync internals | Leader priority (explicit > playing > internal clock); rate formula; beat-distance phase, ±5% cap | `docs/03` / `docs/11` |
| Auto-mix | Automation ladder: Sync → Quantize → Auto-Mix (silence-detected intro/outro cues, phrase-aligned switch points) | `docs/11` + `docs/10-autopilot-show-rules.md` |
| Latency | Target < 10 ms out; default 256–512-sample buffer (user-settable); ASIO/WDM-KS (Win), CoreAudio (mac) | `docs/01-audio-source-layer.md` |
| RT safety | Audio thread: no alloc, no locks, no I/O; lock-free queue for `PerformanceAction` → audio | `docs/01` / `docs/04-performance-action-system.md` |
| **A/V coupling** | **One internal Link-style beat timeline (hostTime, beatTime, tempo) + quantum drives BOTH audio & visuals; quantized launch for visual changes; optional external Ableton Link interop** | `docs/00`, `docs/03`, `docs/08-visual-performance-engine.md`, `docs/04` |

---

## Sources (this round)

**Primary / authoritative:** Mixxx manual & developer wiki (Master-Sync, Auto DJ, Sound
Hardware / latency, key & beat detection, audio-engine callback docs); Ableton Link
documentation & `link.docs` (timeline, quantum, `phaseAtTime`/`beatAtTime`); Krumhansl &
Kessler (1982) and David Temperley (probabilistic key finding); *Automatic Detection of Cue
Points for the Emulation of DJ Mixing* (Computer Music Journal, MIT Press); Ross Bencina,
"Real-time audio programming 101" (RT-thread rules, latency context); PortAudio API docs;
Microsoft low-latency audio / WASAPI `IAudioClient3` docs; libkeyfinder (GitHub); Resolume
docs + zerotovj (Resolume↔Link); Mixed In Key (Camelot wheel).
**Method:** 24 sources → 203 extracted claims → adversarial verification (122 votes, ~95%
pass). Final synthesis reconstructed manually after the synthesis agent crashed.
