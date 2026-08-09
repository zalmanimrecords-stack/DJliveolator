# 02 — Core domains

- **Purpose:** the domain boundaries, what each owns, and how they depend on one another.
- **Scope:** `Liveolator.Core` domain folders and their principal application, persistence, native and MCP consumers.
- **Source of truth:** `src/Liveolator.Core/**`, `src/Liveolator.App/Composition/ServiceConfig.cs`.
- **Last validated:** 2026-08-01 (against commit `6a32b80`)
- **Confidence:** High — boundaries are explicit in namespaces, seam interfaces, handler ownership and tests.
- **Related:** [overview](./01-system-overview.md) · [entities and rules](./03-business-entities-and-rules.md) · [hotspots](./10-business-logic-hotspots.md)

## Domain map

| Domain | Business purpose | Main paths | Driven by | Depends on |
| --- | --- | --- | --- | --- |
| Performance actions | Normalise every intent and route each kind to exactly one owner | `Core/Actions` | UI, MIDI, studio, autopilot | All engine handlers |
| Beat and synchronisation | Tempo, beat and bar phase, tap, lock, quantised scheduling, the shared timeline | `Core/Beat`, `Core/Audio/Sync` | Audio frames, deck state | Decks, visuals, playlist |
| Decks | Playback intent, cue and hot cues, loops, jog and bend, key lock, sync | `Core/Audio` | Actions | `IDeckEngine` in `Liveolator.Audio` |
| Mixer | Crossfade, per-channel gain, three-band EQ, filter, cue bus, master limiter | `Core/Mixer` | Actions | `IMixer` in `Liveolator.Audio` |
| Library and analysis | Scan, classify, query, analyse, repair, relocate and import media | `Core/Library`, `Core/Analysis` | UI, MCP | Decoders, metadata readers, catalog store |
| Playlist and harmonic planning | Per-deck Now/Next/Later queues and compatible ordered sets | `Core/Playlist` | Actions, UI, MCP | Beat scheduler, deck loader |
| Visual performance | Banks, scenes, layers, macros, effects, generators, track-linked cues | `Core/Visuals` | Actions, beat clock | GL and media adapters |
| Autopilot | Evaluates triggers, conditions, cooldowns and override policy, then emits actions | `Core/Autopilot` | A tick context supplied by a host | Dispatcher |
| Studio | Timeline clips, automation lanes, tempo curves, render planning | `Core/Studio` | UI timeline, host clock | Dispatcher, offline renderer |
| Extensions and skins | Validate, trust, install, enable and load packaged capabilities | `Core/Extensions`, `Core/Skins` | Settings, UI | File and signature infrastructure |
| Persistence, settings, update | Store authored and live state; decide whether to offer an update | `Core/Persistence`, `Core/Settings`, `Core/Update` | Every domain | `Liveolator.Media`, `Liveolator.Online` |
| Agent interface | Expose selected library, analysis, harmonic, playlist and visual use cases | `src/Liveolator.Mcp` | External AI client | Core services and shared stores |

## Ownership boundaries

The action domain is the one deliberate cross-domain seam: it is how a gesture reaches an engine, and
it is why the same rule applies whether the gesture came from a mouse, a controller or a timeline.
Everything else is reached through a seam interface owned by Core and implemented outside it.

Persistence interfaces stay in Core; the JSON and SQLite implementations live in `Liveolator.Media`.
Native audio, MIDI, OpenGL, FFmpeg, filesystem and HTTP concerns are adapters, never policy.

## Architectural observations

- **Autopilot has no host.** `AutopilotEngine` is complete and tested, but nothing outside
  `Core/Autopilot` constructs it or supplies an `AutopilotTickContext`. The only other references in
  the repository are persistence: `ILiveProfileStore.SaveAutopilotRuleSetAsync` /
  `LoadAutopilotRuleSetAsync` and their `LiveProfileStore` implementation. The domain is therefore
  present, persistable and unreachable at runtime. Coverage row in [06](./06-ui-feature-coverage.md).
- **Visual scene authoring has no writer.** `ServiceConfig.LoadBanksOrStarter` reads visual banks from
  `ILiveProfileStore` and falls back to a code-built starter bank; no application code saves a
  `VisualBank`. Banks and scenes are consumed, not authored, by the product.
- **Some product policy sits in App view models** because it depends on presentation workflow —
  played-history presentation, load-versus-queue coordination, startup restoration. These are
  product-significant even though they are outside Core; evidence in
  [10](./10-business-logic-hotspots.md).
- **`ServiceConfig` is intentionally orchestration-heavy** and should not be read as a domain
  service.
