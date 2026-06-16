# File Ownership & Hot Files

> Which files are safe to edit in parallel, and which are not. A "hot file" is a shared
> seam/registry that many features touch; two agents editing one in parallel collide on merge
> (we saw this on `PerformanceActionKind.cs`). Hot files are edited by **one task at a time**,
> or by the **orchestrator** on the integration branch before fan-out.

## Hot files (serialize or orchestrator-only)

| File | Why it's hot | Rule |
|------|--------------|------|
| `src/Liveolator.Core/Actions/PerformanceActionKind.cs` | Every feature appends action kinds; merges collide on the enum tail | Orchestrator appends needed kinds up front, OR one task at a time. **Append only** (preserve serialized values). |
| `src/Liveolator.App/Composition/ServiceConfig.cs` | The single DI root; every feature registers here | One task at a time; keep edits to small, additive blocks. |
| `src/Liveolator.Core/Audio/IMultiDeckPlaybackEngine.cs` | Deck seam implemented by 3+ types; adding a member forces all implementers | Orchestrator coordinates; the task adding a member updates ALL implementers in the same branch. |
| `src/Liveolator.App/App.axaml`, `src/Liveolator.App/Theme/Spartan.axaml` | Shared brush/style tokens | One task at a time. |
| `*.sln`, `Directory.Build.props`, any `*.csproj` | Build graph | Orchestrator-only. |
| `docs/18-implementation-status.md` | The living status map everyone updates | Orchestrator updates after each merge. |
| `orchestration/TASKS.md` | The ledger | Orchestrator-only. |

## Implementers that must move together

Adding a member to a shared seam means updating every implementer **in the same branch**, or
the build breaks. Known sets:

- **`IMultiDeckPlaybackEngine`** implementers: `TwoDeckBassEngine` (Audio, partials),
  `SingleDeckEngineAdapter` (Core), plus test fakes `FakeMultiDeckPlaybackEngine` (Audio.Tests)
  and the `FakeMultiDeckEngine` inside `DeckActionHandlerTests` (Core.Tests).
- **`IBassMixerBackend`** implementers: `BassMixerBackend` (Audio) + its test fake.
- **`IVisualPerformanceEngine`** implementers: `GlVisualPerformanceEngine` (Visuals) + fakes.

## Area ownership (safe-to-parallelize lanes)

These rarely overlap, so tasks scoped to one area can run concurrently:

| Lane | Roots | Typical tasks |
|------|-------|---------------|
| Audio engine | `src/Liveolator.Audio/Playback/**` | key-lock native, cue, loops |
| Beat/sync | `src/Liveolator.Core/Audio/Sync/**`, `Core/Beat/**` | phase-lock, quantize |
| Library | `src/Liveolator.Core/Library/**`, `src/Liveolator.Media/**` | dedup, relocate, scan |
| Visuals/GL | `src/Liveolator.Visuals/**` | effects, compositor |
| App UI (feature) | `src/Liveolator.App/Features/<Feature>/**` | one feature folder per lane |
| Mapping/MIDI | `src/Liveolator.Core/Mapping/**`, `src/Liveolator.Midi/**` | controller profiles, learn |
| Studio | `src/Liveolator.App/Features/Studio/**`, `Core/Studio/**` | timeline, automation |

**Rule:** a task may run in parallel only if its file set is disjoint from every other
in-progress task's set **and** it claims no hot file another task needs.
