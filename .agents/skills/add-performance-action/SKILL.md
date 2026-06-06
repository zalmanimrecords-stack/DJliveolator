---
name: add-performance-action
description: Add a new PerformanceAction to Liveolator end-to-end through the dispatcher seam — new PerformanceActionKind, the owning concern handler, DI registration, and the emitting source — TDD-first. Use when adding a transport/beat/visual/deck/mixer/playlist command, wiring a controller or UI control to an engine, or whenever intent must reach an engine without a direct call.
---

# Add a PerformanceAction (the seam workflow)

In Liveolator, **hardware, UI, and autopilot never call the engines directly** — they
emit a serializable `PerformanceAction` that the dispatcher routes to exactly one
concern handler. Adding a command means extending that seam, not adding a call.

Authoritative design: [`docs/04`](../../../docs/04-performance-action-system.md).
Real types: `src/Liveolator.Core/Actions/`.

## Before you start

- Decide the **concern** (transport, beat, visual, deck, mixer, playlist, …) — it
  determines which handler owns the new kind.
- Decide the **input mode**: `Momentary`, `Toggle`, `Absolute` (knob/fader 0..1), or
  `Relative` (encoder delta).
- Is it **beat-quantized** (`…NextBeat` / `…NextBar`)? If so the handler must defer it
  through the beat scheduler (doc 03), not apply it immediately.

## Steps (TDD-first)

1. **Write the test first** under `tests/Liveolator.Core.Tests/Actions/`. Use the
   existing doubles in `ActionTestDoubles.cs` (`FakeActionHandler`, `CapturingLogger<T>`)
   and follow `PerformanceActionDispatcherTests.cs`. Assert:
   - `Dispatch(newKind)` reaches the owning handler exactly once with the right
     `Value`/`Slot`/`InputMode`;
   - feedback flows back through `FeedbackChanged` / `GetFeedback`;
   - a throwing handler is **logged, not rethrown** (the dispatcher swallows + logs).

2. **Add the kind** to `PerformanceActionKind.cs`, in its concern group (keep the
   grouping comments from doc 04). The `PerformanceAction` record stays serializable —
   use only the existing primitive fields (`Value`, `Slot`, `Argument`); do not add
   reference-typed payloads.

3. **Add it to the owning handler** (a `PerformanceActionHandlerBase` subclass):
   - put the kind in `HandledKinds` — **exactly one** handler may own a kind, or the
     dispatcher throws at construction by design;
   - apply it to the engine in `Handle(action)`. For quantized kinds, defer through the
     beat scheduler instead of acting immediately;
   - if it has state (toggle/knob), override `GetFeedback` and call `RaiseFeedback(...)`
     when the engine state moves, so LEDs/UI update without polling.
   - If no handler owns this concern yet, create one small focused handler for it
     (one concern per handler — no giant switch).

4. **Register the handler in DI** at `src/Liveolator.App/Composition/ServiceConfig.cs`
   so `PerformanceActionDispatcher` discovers it via the injected handler set.

5. **Emit the action from the source** (UI command, controller mapping, or autopilot)
   by calling `IPerformanceActionDispatcher.Dispatch(...)`. Never call the engine
   directly from the source.

## Guardrails

- The dispatcher already wraps handler calls in try/catch with the action in the log
  context and marshals feedback to the UI thread — handlers and callers stay
  thread-agnostic. Don't re-implement that.
- Don't add a per-kind `switch` anywhere — routing is data owned by handlers.
- Unknown/unhandled kinds are logged as a warning, never thrown — keep it that way.

## Validate

```powershell
dotnet build
dotnet test
```

Confirm the new dispatch + feedback tests pass and no duplicate-kind exception fires.
