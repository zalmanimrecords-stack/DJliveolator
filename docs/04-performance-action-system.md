# 04 — Performance Action System

> **✅ Status (2026-06-05): BUILT in `Liveolator.Core/Actions/`** — see
> [`18-implementation-status.md`](18-implementation-status.md). The full model
> (`PerformanceAction`/`Kind`/`ActionInputMode`), the dispatcher (`PerformanceActionDispatcher`
> with handler-registration routing, no giant switch), the handler seam
> (`IPerformanceActionHandler` + base), feedback (`ActionFeedbackState`/`Changed`), and the
> UI-marshaling seam are implemented and tested. **`BeatActionHandler` is the first real
> handler** (doc 03). Pending: Transport/Visual/Deck/Mixer/Playlist handlers, which land with
> their engines. **Do not rebuild the dispatcher or the model.**

## Purpose

One command model for every source of intent — UI, Push, DJ controllers, keyboard,
and autopilot — so they all drive the same engines through a single dispatcher. This
is the heart of the action-layer principle (doc 00).

## Existing code this touches

- `RelayCommand` / click handlers in `UI.Analog` — today the UI calls engines (deck,
  preset, overlay) directly. Selected commands are re-routed through the dispatcher
  so the UI becomes just another action source (no behavior change to the user).
- `TapeDeckViewModel`, preset navigation, `OverlayFxPanel` — become action handlers
  rather than direct callers.

## Action model

```csharp
public enum PerformanceActionKind
{
    // Transport
    TransportPlayPause, TransportStop, TransportNextTrack, TransportPreviousTrack,
    TransportQueueTrack, TransportLoadSelectedTrack, TransportToggleAutoAdvance,
    // Beat
    BeatTapTempo, BeatLock, BeatUnlock, BeatHalfTempo, BeatDoubleTempo,
    BeatNudgeForward, BeatNudgeBackward, BeatResetGrid, BeatSetDownbeat,
    // Visual (compositor model — doc 08; no projectM presets)
    VisualLoadScene, VisualSelectBank, VisualSetMacro,
    VisualSetLayerSource, VisualToggleLayer, VisualSetLayerOpacity, VisualLaunchClip,
    VisualBlackout, VisualToggleStrobe,
    VisualTransitionNow, VisualTransitionNextBeat, VisualTransitionNextBar,
    // Deck / DJ (doc 11) — driven only via actions
    DeckLoadTrack, DeckPlayPause, DeckCue, DeckHotCue, DeckSetLoop, DeckSeek,
    DeckPitch, DeckSyncOnce, DeckQuantizeToggle,
    // Mixer (doc 11)
    MixerCrossfade, MixerChannelGain, MixerEqBand, MixerFilter, MixerCueToggle,
    // Auto-mix (doc 11) — hands-free assist
    AutoMixToggle, AutoMixSkipToNext,
    // Playlist
    PlaylistInsertTrackNext, PlaylistMoveTrack, PlaylistRemoveFutureTrack,
    PlaylistSkipOnNextBar,
}

public enum ActionInputMode { Momentary, Toggle, Absolute, Relative }

public sealed record PerformanceAction(
    PerformanceActionKind Kind,
    ActionInputMode InputMode = ActionInputMode.Momentary,
    double Value = 0,            // absolute 0..1, or relative delta
    int Slot = 0,                // scene/bank/overlay/track index where relevant
    string? Argument = null);    // macro name, etc.
```

Design requirements (from the plan):

- **Serializable** — actions are plain records, so mappings and show profiles can be
  saved (doc 13). The enum + primitive fields serialize cleanly to JSON.
- **Input modes** — momentary (button down), toggle, absolute (a knob/fader value),
  relative (an encoder delta). A single `Kind` can accept the mode appropriate to
  the control bound to it.
- **Feedback** — actions report state back for LEDs and UI indicators.

## Dispatcher

```csharp
public interface IPerformanceActionDispatcher
{
    void Dispatch(PerformanceAction action);
    ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot = 0);
    event EventHandler<ActionFeedbackChanged>? FeedbackChanged;
}

public sealed record ActionFeedbackState(
    bool IsActive,        // toggle on / armed
    bool IsAvailable,     // can be triggered now
    double Value);        // current value for knob-backed actions
```

- The dispatcher routes each `Kind` to the owning engine via small handler
  registrations (one handler per concern — transport handler, beat handler, visual
  handler, playlist handler). No giant switch in one file (global standards #2, #3).
- Beat-quantized kinds (`...NextBeat`, `...NextBar`, `SkipOnNextBar`, `DeckQuantizeToggle`,
  quantized `VisualLaunchClip` / `VisualLoadScene`) are not applied immediately; the handler
  defers them through `IBeatScheduler` (doc 03).
- **Unified audio↔visual timing (doc 00 differentiator):** because audio handlers (sync,
  auto-mix) and visual handlers both defer through the *same* `IBeatTimeline` (doc 03),
  quantized audio and visual actions land on the same beat/bar grid. A single mapped control
  can therefore trigger an audio *and* a visual action that fire together on the next
  quantum — this is the mechanism behind "control both simultaneously."
- UI-affecting actions are marshaled to the UI thread inside the dispatcher so
  handlers and callers stay thread-agnostic (doc 00).

## Feedback for LEDs and UI

`FeedbackChanged` lets controllers light pads/LEDs (Push, doc 06) and the UI reflect
armed/active/value state without polling. Example: `BeatLock` toggle feedback drives
both the Push lock button LED and the DJ Sync module `LOCK: ON` readout (doc 12).

## Why a dispatcher and not direct calls

- Decouples input from engines: adding a controller never touches engine code
  (global standard #4).
- Makes every action testable in isolation: a dispatcher test can assert that
  `Dispatch(VisualLoadScene)` calls the visual engine once, with no MIDI or UI
  present (doc 14).
- Enables autopilot (doc 10) and macro recording later for free — they emit the same
  actions.

## Error handling & logging

- Each handler wraps its engine call in try/catch with the action kind in the log
  context; a failing action is logged and surfaced as feedback, never silently
  dropped (global standards #16, #26).
- Unknown/unhandled kinds are logged as a warning rather than throwing.

## Phase

Phase 4. Route `VisualLoadScene`, `VisualBlackout`, `BeatTapTempo`, `BeatLock`,
`TransportNextTrack` through the dispatcher first (the plan's success criteria), then
migrate the rest (deck/mixer/sync/auto-mix actions land with Phase 10, doc 11).

## Risks

- Over-routing too early couples the dispatcher to half-built engines. Start with the
  five proven actions and grow the enum as engines land.
- Quantized actions depend on the beat engine (doc 03) being trustworthy; until then
  they fall back to immediate execution with a logged note.
