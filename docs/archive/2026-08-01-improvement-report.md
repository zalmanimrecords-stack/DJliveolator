> **Archived on 2026-08-01. Not current.** Current documentation lives in `docs/core-business-logic/`.

# Code Improvement Report
_Generated: 2026-07-18 (loop 3)_

## Summary
An autonomous maintenance pass over `Liveolator.Core` (the pure, hardware-free, fully
unit-tested layer — the safe zone for refactoring). The codebase remains in unusually
good shape: no `TODO`/`FIXME`/`HACK` anywhere in `src`, zero empty catch blocks, no
proven dead private/internal members, and all-but-one doc cross-reference resolves. The
one substantial finding was a genuine DRY violation — the short-time-Fourier-transform
(STFT) framing scaffold was hand-copied verbatim across all four spectral analyzers,
with a documented "must line up frame-for-frame" coupling enforced only by comment. That
was extracted into a shared, tested helper and all four consumers routed through it. Two
long-function candidates were also decomposed, and one stale doc link fixed. Six safe,
evidence-backed commits; everything riskier (audio/GL hot loops, roadmap enum stubs) was
deliberately deferred rather than gambled on.

## Commits Made
| # | Commit | Type | Scope |
|---|--------|------|-------|
| 1 | `4d41e0b` drop dangling link to non-existent `PresetOptionViewModel` | docs | visuals |
| 2 | `f0279b8` add shared `Stft` framing helper for spectral analyzers | refactor | dsp |
| 3 | `c33c020` route onset envelopes through the shared `Stft` helper | refactor | bpm |
| 4 | `9f65e67` route band-energy + chroma through the shared `Stft` helper | refactor | analysis |
| 5 | `e8be4cb` extract `BuildBandChains` from `WaveformBuilder.Build` | refactor | waveform |
| 6 | `1c978e9` extract `ClassifyTrack` from `LibraryDoctor.Scan` | refactor | library |

## Validation Commands Run
- `dotnet build src/Liveolator.Core -c Debug` — passed (0 warnings, 0 errors) [baseline]
- `dotnet build src/Liveolator.Core -c Release` — passed (0 warnings, 0 errors) [CI-matching, final]
- `dotnet test tests/Liveolator.Core.Tests -c Debug` — **1509 passed, 0 failed, 0 skipped** (final)
- Per-commit narrow validation: `--filter Dsp.StftTests` (15), `--filter Waveform` (25),
  `--filter Library` (151), plus two full-suite runs after the BPM and band/chroma routings.

Baseline before any change: Core build green, **1494 passed / 0 failed / 0 skipped**.
Net after loop: 1509 passed (the +15 are the new `StftTests`). No behavior change.

Note: the only uncommitted files on entry were CRLF/line-ending normalizations under
`.claude/` (config + skill markdown) — pre-existing, environment-driven, left untouched.

## Improvement Candidates Found

### needs-refactor-now (addressed)
- **`docs/28-controllable-preset-generator-addon.md:195`** — Phase 6 "✅ DONE" linked
  `PresetOptionViewModel.cs`, which exists nowhere in `src`. → Removed the dead link;
  the sibling `PresetControlsViewModel.LoadPreset` already builds the ≤5 knobs the doc
  describes (the design consolidated into one view-model). *(commit 1)*
- **STFT framing duplication** — the identical Hann-windowed frame slide +
  `Fft.MagnitudeSpectrum` scaffold and the byte-identical power-of-two/hop constructor
  guard were hand-copied in `OnsetEnvelope`, `PercussiveOnsetEnvelope`,
  `BandEnergyEnvelope`, and `ChromaExtractor`. → New `Dsp/Stft.cs`
  (`ValidateFrameParams` / `FrameCount` / `ForEachFrame`) with dedicated tests; all four
  routed through it, so the frame-for-frame alignment now holds by construction rather
  than by comment. *(commits 2–4)*
- **`WaveformBuilder.Build` (~87 lines)** — mixed LR4 band-filter construction with the
  per-bucket peak-reduction loop. → Extracted `BuildBandChains`. *(commit 5)*
- **`LibraryDoctor.Scan` (~114 lines, longest method in Core)** — mixed five concerns.
  → Extracted the four-branch per-track health check into `ClassifyTrack`. *(commit 6)*

### worth-refactoring-soon (deferred)
- **`LibraryDoctor.Scan` remaining concerns** — offline-folder detection, visual-asset
  checks, duplicate grouping, and report assembly could each become a named helper.
  Deferred to keep this loop's diff small and one-concern-per-commit.
- **STFT attack/release smoother** (`Audio/AudioLevelEnvelope.cs:65`,
  `Audio/FrequencyBandEnvelope.cs:44`) — same exponential-smoother idiom, but only ~3
  shared lines and wired in differently (scalar VU vs per-band). Extract a shared
  `AttackReleaseSmoother` only if a third caller appears (below the ~10-line bar today).

### acceptable-as-is
- **`TempoEstimator.Estimate`** — long but cohesive; the autocorrelation sweep is
  deliberately *fused* with the argmax + mean accumulation in one pass. Extracting it
  would add a redundant scan and de-optimize a hot analysis path for marginal gain.
- **`MasterLimiter.Process`** — hot realtime audio loop that intentionally keeps
  allocation/indirection out; the class comments say so. Leave inlinable.
- **`DeckActionHandler.Handle` (~145 lines)** — the longest method in Core, but it's a
  dispatcher `switch` already delegating to small per-case methods; no computation to
  extract.
- **Effect-processor boilerplate** (`FreeverbProcessor`, `PhaserProcessor`,
  `MoogLadderFilterProcessor`) — idiomatic `IAudioEffectProcessor` shape; the DSP bodies
  are unrelated.

### unclear-needs-more-evidence
- *(none)* — every candidate examined resolved to one of the categories above.

## Areas Intentionally Left Unchanged
- **Roadmap enum stubs** (grep-proven unused but deliberate public API):
  `BeatClockSource.External` (planned Ableton Link), `TransitionStyle.Wipe`/`Dissolve`,
  `TrackVisualFallback.SolidColor`. Removing planned-feature API is an owner decision,
  not a mechanical cleanup — left in place.
- **Everything outside `Liveolator.Core`** — App/Visuals/Audio/etc. carry native/UI/GL
  surface that can't be validated headlessly here; refactoring them without the running
  app is riskier than the reward for this pass.
- **The `.claude/` CRLF churn** — pre-existing, not ours.

## Risks and Follow-up Items
- The STFT dedup introduces a per-frame delegate invocation. All four consumers are
  **offline** analysis paths (BPM/key/cue/HPSS), never the realtime clock, so the cost is
  negligible — but if any consumer is ever moved onto the realtime path, revisit the
  callback shape (e.g. a `ref struct` enumerator) before doing so.
- `PresetOptionViewModel` was documented as shipped but never existed. Worth a quick
  owner confirm that the consolidated `PresetControlsViewModel` fully covers the intended
  Phase 6 scope (it appears to).

## Suggested Improvements (next loop)
- Finish decomposing `LibraryDoctor.Scan` (offline-folders / visuals / duplicates /
  report assembly into named helpers), one concern per commit.
- Extend the STFT helper: consider a shared overload that also owns the spectrogram
  accumulation pattern used by `PercussiveOnsetEnvelope` if a second spectrogram consumer
  appears.
- Run a coverage pass on the Core dirs that are sparse by test-file count
  (`Autopilot`, `Persistence`, `Enrichment`) to confirm the pure logic there is actually
  exercised, not just reachable — add characterization tests where it isn't.

## Topics for Treatment
- **Roadmap-stub policy** — decide whether unreferenced public enum values
  (`External`/`Wipe`/`Dissolve`/`SolidColor`) stay as planned API or get removed until
  their feature lands; a one-line `// reserved` convention would make intent explicit and
  stop future dead-code scans from re-flagging them.
- **Delegate-vs-span framing** — a project stance on when a shared DSP helper may use a
  callback (offline) vs. must stay allocation-free (realtime).

## New Feature Ideas
- The `Stft` helper is now a natural home for a **reusable spectrogram/feature front-end**
  if more analyzers arrive (e.g. spectral-centroid or MFCC-based features for smarter
  auto-cue or genre hinting) — they'd get consistent framing for free.
- `LibraryDoctor` already classifies track health; a **"fix all safe issues" batch
  action** (relocate obvious moves, drop confirmed-missing) is a small step from the
  existing `Preview` / `LibraryRepairPlan` scaffolding.
