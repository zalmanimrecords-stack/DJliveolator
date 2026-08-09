# 10 — Business logic hotspots

- **Purpose:** evidence about where logic is concentrated, coupled, risky or untested. Evidence only — proposed treatments are in [15](./15-refactor-recommendations.md).
- **Scope:** the whole repository, weighted toward code whose failure is audible or destroys user data.
- **Source of truth:** file sizes and call sites at commit `6a32b80`.
- **Last validated:** 2026-08-01 (against commit `6a32b80`)
- **Confidence:** High for the measurements; Medium for the impact assessments, which are engineering judgement grounded in code structure.
- **Related:** [domains](./02-core-domains.md) · [rules](./03-business-entities-and-rules.md) · [refactors](./15-refactor-recommendations.md)

| Location | Size | Why it is a hotspot | Business impact if it breaks |
| --- | --- | --- | --- |
| `src/Liveolator.App/Composition/ServiceConfig.cs` | 1468 lines | The single composition root: it constructs every engine, handler, store and view model, restores session state, loads visual banks and sequences startup. Any business decision that lands here becomes untestable and unreusable | The app does not start, or starts with a silently degraded engine |
| `src/Liveolator.App/Features/Libraries/LibrariesViewModel.cs` | 1964 lines | The largest single file in the repository. Scan, search, facets, badges, enrichment, import, repair, deck loading and playlist building all converge in one presentation type | Library management becomes unmaintainable; repair and relocation are among the destructive paths |
| `src/Liveolator.Core/Audio/DeckActionHandler.cs` | 584 lines | Owns roughly 30 action kinds — transport, cue, hot cues, loops, jog, bend, key lock, quantize, sync, grid edits — plus slot addressing, feedback and native-engine coordination | Every deck gesture; failures are immediately audible |
| `src/Liveolator.Core/Actions/PerformanceActionDispatcher.cs` | 139 lines | Small, but the widest blast radius in the system: unique ownership, feedback semantics, failure swallowing and serialisation compatibility all depend on it | Nothing reaches any engine |
| `src/Liveolator.Core/Library/Music/MusicLibrary.cs` + `Analysis/TrackAnalyzer.cs` | 486 + 150 lines | An expensive partial-failure workflow combining filesystem identity, decoding, metadata, analysis provenance and persistence | A scan that corrupts or loses catalog state costs the user hours of re-analysis |
| `LivePlaylist` + `PlaylistActionHandler` + `DeckTrackLoader` + `PlaylistAudioPlayer` | 4 files across Core and Audio | The queue invariants span a project boundary, and the audio player advances Now from an engine callback — a concurrency seam | A dropped or double advance is a silent-floor moment |
| `src/Liveolator.Core/Mixer/MixerActionHandler.cs` | 405 lines | Crossfade, gain, three-band EQ, filter, cue bus, EQ cut mode and the master limiter in one handler, each re-pushing coefficients to the native mixer | Audible level and tone errors on every channel |
| `StudioArranger` + `StudioTransport` + `TempoCurve` + render planning | 4 areas | Boundary-crossing timing maths: interpolation and clip-edge detection can produce audible errors while every state remains individually valid | Wrong renders and wrong live playback of an arrangement |
| `ExtensionPackageValidator` + `ExtensionInstaller` (198 lines) | Security-sensitive | Archive extraction, path containment, signature and hash checking, dependency resolution and atomic install — with a deliberate developer-mode bypass | Arbitrary content activation is the worst-case outcome in this product |
| `LibraryDoctor` + `LibraryReferenceRewriter` | Destructive | The only code paths that rewrite references across playlists, cues, projects and visual programmes, and that can remove user files | Irrecoverable loss of user media or of authored data |
| `AutopilotEngine` | 164 lines | Rule-dense and show-critical, with seeded randomness and an override state machine — and no test of it running inside the product, because nothing runs it | Would be a show-stopping failure the first time it is wired up |

## Coupling observations

- **Presentation-resident policy.** Product-significant decisions still live in App view models
  because they depend on workflow: played-history reconstruction, load-versus-queue coordination
  (`DjViewModel`, 265 lines), and startup restoration. These behave like domain rules but cannot be
  reused by MCP or automation and are only testable through the headless UI harness.
- **The dispatcher is the only sanctioned coupling.** Every other cross-domain path goes through a
  seam interface. That is why the deck-count and queue invariants can be stated once
  ([03](./03-business-entities-and-rules.md)) rather than per caller.
- **Two projects share the queue invariant.** `LivePlaylist` owns the rule, `PlaylistAudioPlayer` in
  `Liveolator.Audio` owns the advance trigger. The invariant is therefore only as strong as the
  agreement between two assemblies.

## Test-coverage gaps

`Liveolator.Core.Tests` is large (182 files) and the pure rules are well covered. The gaps are at the
edges: no test exercises autopilot inside a running host because no host exists, and native timing, LED
feedback and GL rendering are covered only by fake backends and a native-missing fallback contract.
Recorded in [11](./11-open-questions-and-assumptions.md).

Two gaps identified here have since been closed (2026-08-02): the cross-assembly live-queue invariant
and the Library Doctor's health-scan pipeline both now have tests that need no view model — see
[15](./15-refactor-recommendations.md). The file-deletion path was found to be already guarded and
already covered.
