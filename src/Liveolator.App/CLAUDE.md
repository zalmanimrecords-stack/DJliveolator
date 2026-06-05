# Liveolator.App — module rules

**Purpose:** the Avalonia UI shell, views, view-models, and the composition root.

**Design source of truth:** [`docs/12`](../../docs/12-ui-modules.md).

## Iron rules

1. **The UI is just another action source.** Emit `PerformanceAction`s through
   `IPerformanceActionDispatcher`; never call the audio/beat/visual/playlist engines
   directly (doc 04 — the seam). This keeps behavior identical whether intent comes
   from a click, a controller, or autopilot.
2. **No business logic in views/view-models** beyond presentation. Domain logic lives
   in `Liveolator.Core`.
3. **DI / wiring goes in [`Composition/ServiceConfig.cs`](Composition/ServiceConfig.cs)** —
   the single composition root. Register handlers here so the dispatcher discovers them.
4. **No native/GL here** — visuals belong to `Liveolator.Visuals`.

**Tests:** `tests/Liveolator.App.Tests`.
