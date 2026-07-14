# 10 — Autopilot Show Rules

> **✅ Status (2026-06-05): BUILT in `Liveolator.Core/Autopilot/`** — see
> [`18-implementation-status.md`](18-implementation-status.md). Implemented and tested: the rule
> model (`AutopilotRule`/`RuleTrigger`/`TriggerKind`/`RuleCondition`/`Cooldown`), the show
> definition (`AutopilotRuleSet`/`ScenePool`/`AutopilotOverridePolicy`/`OverrideMode`),
> `AutopilotTickContext`, the `IAutopilotEngine` seam, and `AutopilotEngine` — triggers, condition
> gating, per-rule cooldowns, seeded scene-pool selection, and the AutoResume/PauseUntilReenabled
> override state machine. The host drives `Tick(...)` from the clock and calls `OnManualAction()`
> for human actions. **Don't rebuild the rule engine or model.**

## Purpose

Run an unattended visual show from rules while the performer supervises or focuses on
music — with instant manual override at any time.

## Existing code this touches

None new to bypass: autopilot emits `PerformanceAction`s through the same dispatcher
(doc 04) as a human. It reads `BeatClockState` (doc 03) and drives the visual engine
(doc 08). This is why it gets engine integration "for free."

## Rule model

```csharp
public sealed record AutopilotRule(
    string Name,
    RuleTrigger Trigger,            // when to evaluate
    RuleCondition Condition,        // optional gate (energy/confidence/position)
    PerformanceAction Action,       // what to emit
    Cooldown Cooldown);             // minimum time/bars between firings

public sealed record RuleTrigger(
    TriggerKind Kind,               // EveryNBeats | EveryNBars | OnDownbeat | OnTrackPosition
    int N);

public sealed record RuleCondition(
    double? MinConfidence,
    double? MinEnergy, double? MaxEnergy,
    double? TrackPositionFrom, double? TrackPositionTo);   // 0..1 of track

public sealed record AutopilotRuleSet(
    string Name,
    IReadOnlyList<AutopilotRule> Rules,
    ScenePool ScenePool);           // controlled randomness source

public sealed record ScenePool(
    IReadOnlyList<string> SceneNames,   // from a VisualBank (doc 08)
    int CooldownBars);                  // a scene can't repeat within N bars
```

## Engine

```csharp
public interface IAutopilotEngine
{
    bool IsRunning { get; }
    void Start(AutopilotRuleSet ruleSet);
    void Stop();
    // Evaluates rules against each BeatClockState tick; emits actions via dispatcher.
}
```

Inputs a rule can use (from the plan): beat count, bar count, energy, beat
confidence, and track position. Example rules:

- Every 16 bars, if confidence > 0.7, `VisualLoadScene(next-from-pool)` on `NextBar`.
- On downbeat, if energy > 0.8, `VisualToggleOverlay(strobe-layer)` pulse.
- In the last 8 bars of a track (track position > 0.9), bias the scene pool toward
  calmer scenes.

## Controlled randomness (not chaos)

The plan explicitly warns against chaotic randomization. Mechanisms:

- **Scene pools** — autopilot only picks from a curated `ScenePool`, never the full
  preset set.
- **Cooldowns** — a scene/action cannot re-fire within N bars, preventing flicker.
- **Intensity limits** — energy-driven rules are clamped so a loud section doesn't
  trigger everything at once.

Randomness uses a seeded generator so a show can be reproduced/debugged. (The runtime
forbids ambient `Math.random()` in some contexts; the engine takes an explicit seed
in its rule set, which also makes shows deterministic for testing — doc 14.)

## Override semantics (default decided)

Any human action through the dispatcher takes precedence. **Default behavior:** a
manual action **suspends** autopilot for a configurable window, then autopilot
**auto-resumes**. This is the forgiving default — the performer can grab control for a
moment without the show stopping, and never gets "stuck" with autopilot silently off.

```csharp
public sealed record AutopilotOverridePolicy(
    OverrideMode Mode = OverrideMode.AutoResume,  // default
    int ResumeAfterBars = 2);                     // window before auto-resume

public enum OverrideMode { AutoResume, PauseUntilReenabled }
```

- `AutoResume` (default): suspend for `ResumeAfterBars` bars after the last manual
  action, then resume rule evaluation.
- `PauseUntilReenabled`: a manual action stops autopilot until the performer toggles
  it back on.

Both modes are supported; the policy is persisted with the rule set (doc 13) so the
choice is per-show. The master **AUTOPILOT** toggle (Push utility row, doc 06) always
hard-stops it regardless of mode.

> This default was chosen pending the user's preference and can be flipped per show or
> changed globally with no architectural impact — both modes share the same state
> machine.

## Persistence

`AutopilotRuleSet` is JSON-serialized under the Live persistence root (doc 13) and is
part of a saved show/setlist.

## Error handling & logging

- Rule evaluation runs in try/catch per rule; a throwing rule is disabled for the
  session and logged with its name, never stalling the tick loop.
- Each fired action is logged at debug with the rule name for show post-mortems.

## Phase

Phase 9 (Autopilot Show Rules).

Success criteria (plan): the app can run an unattended visual show over a playlist,
and the performer can override any decision instantly.

## Risks

- Rule sets can become hard to reason about; keep v1 to a small, documented trigger
  set and grow deliberately.
- Energy/confidence thresholds are genre-dependent; expose them rather than hardcode
  (the mistake `BpmDetector` makes today).

## Resolved (default)

Override behavior defaults to **auto-resume after a configurable window** (see
"Override semantics" above). Both modes are implemented behind one state machine, so
this default can be revisited at no cost when the user has a preference. No longer a
roadmap blocker.
