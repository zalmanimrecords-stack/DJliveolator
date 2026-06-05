# 09 — Live Playlist Engine

> **✅ Status (2026-06-05): queue model BUILT in `Liveolator.Core/Playlist/`** — see
> [`18-implementation-status.md`](18-implementation-status.md). Implemented and tested:
> `QueueEntry`/`TrackState`, the `ILivePlaylist` seam, and `LivePlaylist` (Load/Append/InsertNext/
> Move/RemoveFuture/SetAutoAdvance/SkipNow/SkipOn/NotifyTrackEnded + `NowChanged`). Editing the
> future never disturbs Now; `SkipOn` defers through `IBeatScheduler`. **Pending (blocked on the
> audio library):** the binding over `PlaylistAudioPlayer` and `NextTrackPreloader`. **Don't
> rebuild the queue model.**

## Purpose

Make the playlist editable *during* performance: a Now/Next/Later queue that can be
reordered, inserted into, and skipped on a beat/bar boundary without interrupting
playback.

## Existing code this touches

- `MilkDropVisualizer.App/Audio/PlaylistAudioPlayer.cs` — current multi-track queue:
  `LoadPlaylist` (replace), `AddToPlaylist` (append), `GoToTrack`, next/prev,
  `AutoAdvance`, `RepeatCurrentTrack`. No mid-track insertion, no crossfade/gapless.
- `MilkDropVisualizer.App/UI.Analog/ViewModels/TapeDeckViewModel.cs` and
  `Adapters/TapeDeckAdapter.cs` — MVVM/adapter over the player; one-way position
  sync. Become the UI binding for the new queue (doc 12).
- `SequenceTriggersViewModel` — sequence triggers module; coexists with the queue.

## Queue model

```csharp
public sealed record QueueEntry(string TrackPath, Guid Id, TrackState State);

public enum TrackState { Now, Next, Later, Played }

public interface ILivePlaylist
{
    QueueEntry? Now { get; }
    IReadOnlyList<QueueEntry> Upcoming { get; }   // Next then Later, in order

    void InsertNext(string trackPath);            // after Now
    void Move(Guid id, int toIndex);              // reorder within Upcoming
    void RemoveFuture(Guid id);                   // cannot remove Now
    void SetAutoAdvance(bool on);

    void SkipNow();                               // immediate
    void SkipOn(Quantize when, int everyN = 1);   // safe skip on beat/bar (doc 03)

    event EventHandler<QueueEntry>? NowChanged;
}
```

`Now` is the playing track; `Upcoming` is the editable future. Editing `Upcoming`
never touches `Now`, so playback continues uninterrupted — the central success
criterion.

## Required behavior (from the plan)

- Now / Next / Later queue model.
- Insert a track right after the current one (`InsertNext`).
- Reorder future tracks while the current track keeps playing (`Move`).
- Remove future tracks safely (`RemoveFuture`; `Now` is protected).
- Skip now or skip on the next beat/bar (`SkipNow` / `SkipOn`).
- Auto-advance toggle (`SetAutoAdvance`).
- Preload the next track where possible (open the NAudio reader ahead of time so the
  handoff is fast).
- Preserve session state across app restart (doc 13).

## Relationship to `PlaylistAudioPlayer`

`ILivePlaylist` is a thin live-editing layer **over** `PlaylistAudioPlayer`, not a
rewrite. It owns the ordered `Upcoming` list and drives the underlying player's
`GoToTrack`/auto-advance. This preserves existing playback behavior (global
standard #7) and keeps file/stream handling in the proven player.

Quantized skip uses `IBeatScheduler` (doc 03): `SkipOn(NextBar)` schedules the
`GoToTrack` to fire on the next bar boundary so transitions stay musical.

## Preload

A `NextTrackPreloader` opens the upcoming track's reader (and optionally pre-buffers)
so advancing is near-instant. It must be cancel-safe when the upcoming track changes
due to a live reorder.

## Future behavior (designed, deferred)

- Track analysis cache: BPM, beatgrid, waveform, key, energy (`TrackAnalysisCache`,
  doc 13) — enables instant beat lock on load and per-track scenes.
- Track-specific visual scenes / cue points (link a `VisualScene`, doc 08, to a
  track).
- Setlist / show profile export (doc 13).

## Error handling & logging

- File-open / preload failures are caught, logged with the track path, and surfaced
  in the UI; a bad upcoming track is skipped rather than killing playback
  (global standards #16, #26).
- Reorder/remove operations validate the target id and ignore stale ids from a
  laggy UI without throwing.

## Dual-deck note (Phase 10)

Since Zalmanolator will be the DJ player (doc 11), Phase 10 reinterprets this queue as
a **shared library/crate** the performer loads onto Deck A or Deck B (Load A / Load B),
rather than one auto-advancing list. The Now/Next/Later editing model and safe-skip
primitives still apply per deck. The Phase 7 single-deck design below is the
foundation; the dual-deck refinement is specified in doc 11.

## Phase

Phase 7 (Live Playlist / Assisted Performance): Now/Next/Later, reorder while
playing, insert-next, remove-future, safe skip on beat/bar.

Success criteria (plan): performer runs auto-play and still changes the upcoming
playlist live, with no playback interruption when editing future tracks.

## Risks

- No crossfade/gapless today; track changes reset the audio player. Safe skip on a
  bar boundary mitigates the audible gap, but true gapless/crossfade is a Deck A/B
  concern (doc 11).
- Preloading two readers raises memory; cap and release eagerly (memory growth over a
  long set is a measured metric, doc 14).
