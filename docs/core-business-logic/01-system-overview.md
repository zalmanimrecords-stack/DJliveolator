# 01 — System overview

- **Purpose:** what the system does, for whom, and where the business logic physically lives.
- **Scope:** all runtime components; excludes build tooling and the marketing website.
- **Source of truth:** `src/**`, `tests/**`, `Liveolator.sln`, `scripts/build-installer.ps1`.
- **Last validated:** 2026-08-01 (against commit `6a32b80`)
- **Confidence:** High for component boundaries and the action layer; Medium for behaviour that only appears with native devices present.
- **Related:** [context](./00-project-context.md) · [domains](./02-core-domains.md) · [flows](./04-critical-flows.md) · [UI coverage](./06-ui-feature-coverage.md)

## What the system does

Liveolator plays and mixes two audio decks, analyses and catalogues a music library, and renders a
GPU visual composition — with audio and visuals driven from the same beat clock. It is a single-user
desktop product; there is no server, no account and no multi-tenancy.

Its architectural centre is a serializable command vocabulary. `PerformanceActionKind` currently
declares 76 kinds (`src/Liveolator.Core/Actions/PerformanceActionKind.cs`); every source of intent —
on-screen control, MIDI controller, studio automation, autopilot rule — constructs a
`PerformanceAction` and hands it to `PerformanceActionDispatcher`, which routes it to the single
handler that owns that kind. No input source calls an engine directly.

## Products and applications

Two executables ship from this repository:

1. **The desktop application** (`Liveolator.App`) — an Avalonia shell of seven tabs over a shared set
   of engines.
2. **The MCP server** (`Liveolator.Mcp`) — a stdio process exposing 22 music-intelligence and
   authoring tools to an external AI client. It reads and writes the same stores as the app but does
   not dispatch performance actions; it cannot drive playback.

A marketing website lives under `website/` and is not part of the product runtime.

## Actors

- **Performer / operator** — drives decks, mixer, visuals, queues, recording and mappings.
- **Studio arranger** — places clips and automation on a two-lane timeline, then plays or renders it.
- **Add-on author / operator** — installs packaged extensions and authors visual and control presets.
- **External AI client** — calls MCP tools over stdio.
- **External metadata services** — AcoustID and a GetSongBPM-compatible provider, when configured.

## Deck count

At this commit the engine addresses **four** deck slots while the product exposes **two**.
`MixerState.DeckCount` is `4`: A = 0 and B = 1 are the live decks the crossfader blends, C = 2 and
D = 3 are hidden STUDIO slots with per-deck gain only. `TwoDeckBassEngine.Decks` and
`MixPlan.DeckCount` both derive from that constant, so the realtime engine and the offline renderer
address four. `MixerMath.DeckOutputGain` gives slots ≥ 2 a unity crossfade factor, which is what
keeps a hidden deck outside the A/B crossfader.

Nothing in the user interface creates a clip on C or D: `StudioViewModel` deliberately builds two
lanes, and `StudioClip.DeckSlot` is documented as A/B. The hidden pair is therefore engine capacity
that no surface reaches — a coverage gap, recorded in [06](./06-ui-feature-coverage.md).

`Needs validation` — **this is mid-change.** An uncommitted change in the working tree at the time of
writing reduces `DeckCount` to `2` and removes `DeckC`/`DeckD` along with the unity-crossfade branch,
updating STUDIO, the render tests and the mixer tests to match. If that lands, this section and the
mixer rule in [03](./03-business-entities-and-rules.md) become "two slots, A and B" and the hidden-deck
coverage gap disappears. Re-validate both against the next commit.

## High-level architecture

```text
 on-screen controls ─┐
 MIDI controller ────┼─► PerformanceAction ─► PerformanceActionDispatcher ─► one owning handler
 studio automation ──┘                                                          │
                                                                                ▼
        shared beat clock ◄──────────────── deck / mixer / playlist / beat / visual / recording
                                                    │                     engines
                                                    ▼
                              BASS audio out · OpenGL stage · MIDI feedback
```

Nine types implement `IPerformanceActionHandler`: deck, mixer, beat, playlist, visual, audio
effects, recording and system volume in Core, plus the playlist audio player in the Audio project.

## Runtime components and how they start

`Program.cs` starts Avalonia; `App.axaml.cs` builds the container through
`Composition/ServiceConfig.cs`, then enforces the first-launch terms gate before the shell appears
(see [04](./04-critical-flows.md)). `ServiceConfig` constructs the audio engine, mixer, dispatcher and
handlers, restores persisted session state, loads visual banks, and registers every view model. When
a native dependency is missing the composition still completes and the shell shows an audio-engine
warning banner rather than presenting decks that silently do nothing.

## Subsystem boundaries

| Layer | Projects | Rule |
| --- | --- | --- |
| Business logic | `Liveolator.Core` | Pure C#. No UI, no native, no platform IO. Unit-testable with no hardware. |
| Orchestration | `Liveolator.App/Composition`, `Shell` | Wiring, startup sequencing, lifetime. |
| Presentation | `Liveolator.App/Features`, `Controls`, `Theme` | Avalonia views and view models. |
| Infrastructure | `Audio`, `Media`, `Midi`, `Online`, `Platform`, `Visuals` | Native and IO implementations of Core seams. |
| Agent surface | `Liveolator.Mcp` | Attributed tools over Core services, returning DTOs. |

## Where core business logic physically lives

Almost entirely in `src/Liveolator.Core` (389 source files), organised by domain folder: `Actions`,
`Analysis`, `Audio`, `Autopilot`, `Beat`, `Dsp`, `Enrichment`, `Extensions`, `Legal`, `Library`,
`Mapping`, `Mixer`, `Persistence`, `Platform`, `Playlist`, `Recording`, `Settings`, `Skins`,
`Studio`, `Update`, `Visuals`, `Waveform`. Persistence interfaces stay in Core while the JSON and
SQLite implementations live in `Liveolator.Media`.

A residue of product-significant policy still lives in App view models where it depends on
presentation workflow — recorded as evidence in [10](./10-business-logic-hotspots.md) and as
recommendations in [15](./15-refactor-recommendations.md).

## Deployment boundaries

The repository packages a self-contained Windows build: `scripts/fetch-bass.ps1` retrieves and
verifies the BASS natives against `scripts/bass-libraries.manifest`, `scripts/build-installer.ps1`
publishes the app, checks critical payload files and drives Inno Setup 6 through
`installer/windows/Liveolator.iss` to produce a per-user installer. User data under the
application-data root is intended to survive upgrade and uninstall.

CI builds and tests on Windows and macOS, but no macOS packaging, notarisation or signing workflow
exists in the repository. `Needs validation` — see [11](./11-open-questions-and-assumptions.md).
