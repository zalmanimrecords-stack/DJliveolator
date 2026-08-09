# 00 — Project context

- **Purpose:** the entry point for a developer or AI agent joining this repository. Summaries and links only.
- **Scope:** the whole repository.
- **Source of truth:** `src/**`, `tests/**`, `.github/workflows/ci.yml`, `scripts/**`.
- **Last validated:** 2026-08-01 (against commit `6a32b80`)
- **Confidence:** High for structure, commands and entry points; Medium for anything requiring native devices at runtime.
- **Related:** [overview](./01-system-overview.md) · [domains](./02-core-domains.md) · [UI coverage](./06-ui-feature-coverage.md) · [open questions](./11-open-questions-and-assumptions.md)

## What this is

Liveolator is a cross-platform desktop DJ + VJ performance application: two audio decks with a
software mixer, an analysed music library, and a GPU visual compositor, all driven off one shared
beat clock. Every intent — click, MIDI control, timeline automation — is expressed as a
`PerformanceAction` and routed through a single dispatcher. Licensed GPLv3+ with a linking exception
for the proprietary BASS audio library (`LICENSE`, `LICENSE-EXCEPTION.txt`).

Detail: [01 — system overview](./01-system-overview.md).

## Applications and packages

One solution, `Liveolator.sln`, nine source projects and nine test projects.

| Runtime | Project | Role |
| --- | --- | --- |
| Desktop app | `src/Liveolator.App` | Avalonia UI, view models, composition root |
| Agent server | `src/Liveolator.Mcp` | MCP stdio server for external AI clients |
| Library | `src/Liveolator.Core` | All platform-agnostic business logic |
| Adapters | `Liveolator.Audio`, `.Media`, `.Midi`, `.Online`, `.Platform`, `.Visuals` | Native and IO bindings |

Per-domain responsibilities: [02 — core domains](./02-core-domains.md).

## Actors

Performer/operator, studio arranger, add-on author, and an external AI client over MCP. There is no
account or role model — see [09 — permissions and roles](./09-permissions-and-roles.md).

## Runtime entry points

- Desktop: `src/Liveolator.App/Program.cs` → `App.axaml.cs` → `Composition/ServiceConfig.cs` (the
  single DI root) → `Shell/MainWindow.axaml`.
- Agent: `src/Liveolator.Mcp/Program.cs` (stdio).
- Shell tabs: LIVE · DJ PRO · STUDIO · VJ · LIBRARIES · ADDONS · SETTINGS
  (`Shell/MainWindowViewModel.cs`). MIDI mapping lives inside the SETTINGS tab, not its own tab.

## Data stores

User data is per-user application data under `Liveolator/`, resolved by
`JsonCatalogStore.DefaultRoot`: the music and visual catalogs (JSON or SQLite), scan folders, hot
cues, and a `live/` tree of mapping profiles, visual banks, macros, track-visual programs and
autopilot rule sets (`LiveProfileStore`). Separated stems and the optional Python environment live
under local application data. Layout and rules: [05 — integrations and side
effects](./05-integrations-and-side-effects.md).

## Key integrations

BASS (realtime audio), RtMidi (MIDI), OpenGL via Silk.NET (visuals), FFmpeg CLI (video/camera/offline
decode), optional Python (stems and structure), optional AcoustID and GetSongBPM (metadata), and a
static HTTP update manifest. All detailed in [05](./05-integrations-and-side-effects.md).

## Core invariants

Each is defined once, in [03 — business entities and rules](./03-business-entities-and-rules.md):

- Exactly one handler owns each action kind; a duplicate claim fails at construction.
- A track never interrupts a playing deck unless the performer explicitly auditions it.
- The live queue's `Now` entry cannot be removed by ordinary queue editing.
- Reanalysis never overwrites a manual beat grid or manual metadata unless overwrite is requested.
- An extension package activates only after path, hash, signature, dependency and trust checks pass.
- An update is offered only for a strictly newer, non-skipped, parseable version.
- Only deck slots A and B are blended by the crossfader; higher slots bypass it.

## Critical flows

Controller input to engine · library scan and analyse · load or queue a track · deck synchronisation ·
studio playback and offline render · first-launch terms acceptance · startup update check. Steps and
failure behaviour: [04 — critical flows](./04-critical-flows.md).

## State models

Autopilot run/override, live-queue entries, deck sync lock, studio transport and clips, extension
installation, and the update decision — all in
[08 — state machines and lifecycles](./08-state-machines-and-lifecycles.md).

## Authorization model

None in the application sense: a local desktop process and a local stdio MCP process, both running
with the operating-system user's authority. The real controls are extension trust and configuration
gating. See [09](./09-permissions-and-roles.md).

## Commands

```sh
pwsh scripts/fetch-bass.ps1                       # Windows: fetch the BASS natives (not vendored)
./scripts/fetch-bass.sh                           # macOS / Linux
dotnet build Liveolator.sln -c Release
dotnet test Liveolator.sln -c Release
pwsh scripts/run.ps1                              # or ./scripts/run.sh
pwsh scripts/build-installer.ps1                  # Windows installer (Inno Setup 6)
```

CI (`.github/workflows/ci.yml`) runs restore, build and test on `windows-latest` and `macos-latest`.
There is no lint, format or separate type-check step; the C# compiler is the type gate.

Tests are xUnit. `Liveolator.Core.Tests` is pure logic with no hardware; audio tests use fake BASS
backends; `Liveolator.App.Tests` uses `Avalonia.Headless`, including a `UiShots` filter that renders
each tab to `artifacts/ui-shots/`.

## Never hand-edit

`src/*/bin`, `src/*/obj`, the `runtimes/` natives fetched by `fetch-bass`, and the
`artifacts*/` directories that appear inside project folders. Catalog and cache files under the
application-data root are regenerable.

## Sensitive and high-risk areas

Extension installation and trust, library repair and relocation (it can rewrite or delete user
files), the offline render and recording paths (they write user files), and online enrichment (it
transmits fingerprints and track metadata). Evidence:
[10 — business logic hotspots](./10-business-logic-hotspots.md).

## Architectural boundaries

`Liveolator.Core` is pure C#: no UI, no native, no platform IO — that boundary is what makes the
business rules testable without hardware, and it is enforced by convention and by the per-project
`CLAUDE.md` adapters. Engines are never called directly; intent flows through the dispatcher.

## Known limitations

Windows is the only packaged platform. Autopilot and visual scene authoring have no UI. The
mapping-learn target list is a fixed subset of the action vocabulary. Full list with evidence:
[06 — UI feature coverage](./06-ui-feature-coverage.md).

## Open questions

Fourteen items currently need a human answer, led by macOS support scope, concurrent catalog access
between the app and the MCP process, and the retention policy for paths, fingerprints and
recordings: [11](./11-open-questions-and-assumptions.md).

## The rest of the set

[01](./01-system-overview.md) · [02](./02-core-domains.md) ·
[03](./03-business-entities-and-rules.md) · [04](./04-critical-flows.md) ·
[05](./05-integrations-and-side-effects.md) · [06](./06-ui-feature-coverage.md) ·
[07](./07-doc-inventory-and-status.md) · [08](./08-state-machines-and-lifecycles.md) ·
[09](./09-permissions-and-roles.md) · [10](./10-business-logic-hotspots.md) ·
[11](./11-open-questions-and-assumptions.md) · [12](./12-glossary.md) ·
[13](./13-executive-summary.md) · [14](./14-final-improvement-report.md) ·
[15](./15-refactor-recommendations.md)
