# 04 — Critical flows

- **Purpose:** the end-to-end paths whose failure would be visible to a performing user or would damage user data.
- **Scope:** paths traceable through Core, application orchestration and adapters.
- **Source of truth:** `src/Liveolator.Core/**`, `src/Liveolator.App/**`, `src/Liveolator.Audio/**`.
- **Last validated:** 2026-08-01 (against commit `6a32b80`)
- **Confidence:** High for sequencing; Medium for native side effects that need a device or GL context present.
- **Related:** [rules](./03-business-entities-and-rules.md) · [side effects](./05-integrations-and-side-effects.md) · [lifecycles](./08-state-machines-and-lifecycles.md) · [UI coverage](./06-ui-feature-coverage.md)

## First launch: terms acceptance

**Objective:** no performance surface is presented before liability terms are accepted.
**Trigger:** application start when the accepted terms version is below `TermsOfUse.CurrentVersion`.

1. `App.axaml.cs` builds the container and creates the main window.
2. `EnforceTermsAcceptanceAsync` shows `TermsOfUseWindow` modally.
3. On accept, the latest settings are re-read and the accepted version is persisted, so a concurrent
   settings change is preserved.
4. On decline — or if the dialog itself fails to show — the window is closed, which runs the normal
   teardown.

**Failure behaviour:** a failed persist is logged, never thrown; a failed dialog fails closed.
**UI entry:** the modal itself, and a read-only copy of the same text in SETTINGS.

## Controller input to engine

**Objective:** a hardware gesture reaches an engine through the same path a click takes.
**Trigger:** a MIDI message on an opened input device.

1. `MidiInputPipeline` receives a library-neutral `MidiMessage`.
2. `MidiControllerRouter` sends it to an active learn session or to mapping.
3. `ControllerMapper` matches a `ControllerBinding`, applies the encoding, curve, inversion and soft
   takeover, and produces a `PerformanceAction`.
4. `PerformanceActionDispatcher` routes it to the one handler that owns the kind.
5. The handler updates its engine and raises feedback; `MidiFeedbackPublisher` drives LEDs and UI
   subscribers update.

**Decisions:** learn versus perform; absolute versus relative; whether soft takeover is still holding.
**Failure behaviour:** an unmatched message is ignored; a throwing handler is logged and the pipeline
survives ([03](./03-business-entities-and-rules.md)).
**UI entry:** SETTINGS → MIDI mapping panel, plus the global learn toggle in the shell.

## Scan and analyse the music library

**Objective:** turn a folder of files into queryable, analysed tracks without losing work on a bad file.
**Trigger:** a scan started from LIBRARIES or the MCP `scan_music_folders` tool.

1. `MusicLibrary` enumerates the configured roots and compares files against the catalog using
   incremental-scan rules, so an unchanged file is not re-decoded.
   A scan only speaks for the folders it walked: a catalogued file outside them is never treated as
   deleted, so scanning one folder leaves the rest of the catalog untouched instead of forcing callers
   to re-walk every folder they have ever scanned. Dropping the entries of a folder that has left the
   scan set stays a separate, deliberate act (`MediaLibrary.PruneToFolders`).
2. Metadata and audio decoders produce a `MusicTrack`; `TrackAnalyzer` derives tempo, beat grid, key,
   cues and confidence where it can.
3. A per-file failure is recorded as analysis status on that track; the scan continues.
4. Each track is persisted as it completes, then query, facet and sort policies project the catalog
   for the UI or for MCP.

**Side effects:** catalog writes, optional online lookups, optional Python analysis — all in
[05](./05-integrations-and-side-effects.md).
**UI entry:** LIBRARIES tab.

## Load or queue a track

**Objective:** stage a track without ever cutting off audio the room is hearing.
**Trigger:** load or double-click in LIBRARIES, the DJ PRO browser, or a deck drop target.

1. The surface picks a deck slot and calls `DeckTrackLoader.Load` with the analysed BPM and downbeat.
2. Reachability, playing-state and audition rules decide the outcome
   ([03](./03-business-entities-and-rules.md)).
3. An idle deck receives `DeckLoadTrack` and then `DeckSetFirstBeat` carrying the downbeat anchor and
   encoded kick onsets; a playing deck receives `PlaylistAppendTrack`.
4. `PlaylistActionHandler` routes by deck slot; `PlaylistAudioPlayer` loads and plays Now and advances
   on that deck's end-of-track event.

**Failure behaviour:** every outcome carries a human-readable message for the status line — a missing
file, an engine that could not open the file, or a queued instead of loaded result. Nothing fails
silently.

## Synchronise two decks

**Objective:** hold two decks in tempo and phase agreement.
**Trigger:** `DeckSyncToggle` or `DeckSyncOnce`.

1. `DeckActionHandler` resolves the sync target and the reference tempo.
2. Tempo matching establishes a compatible rate; the phase controller applies bounded corrections and
   releases them once aligned.
3. `DeckPitchBend` from a jog or nudge slides phase temporarily without moving the pitch fader, and is
   ignored while sync owns the rate.

**State:** `SyncLockState` and `SyncMode`, defined in [08](./08-state-machines-and-lifecycles.md).
**Confidence:** `Needs validation` — exact timing behaviour is adapter-sensitive and is only provable
on real hardware. A proposed contract with acceptance tests exists in `docs/SYNC-BEHAVIOR-SPEC.md`;
it describes intended, not current, behaviour.

## Studio playback and offline render

**Objective:** play or render a timeline arrangement through the same engines a performer drives.
**Trigger:** transport play, or Render, in the STUDIO tab.

1. `StudioTransport` advances project time from an injected host clock.
2. `StudioArranger` emits clip start and stop events as time crosses clip boundaries and interpolates
   automation from keyframes; tempo comes from `TempoCurve`.
3. Resulting actions carry a studio origin and drive the deck and mixer handlers through the
   dispatcher — the same handlers a human drives.
4. Rendering instead builds a `MixPlan`, which `OfflineMixRenderer` consumes to write a file.

**Side effects:** the render writes a user file under the application-data `renders` folder by default.

## Startup update check

**Objective:** offer a newer build without nagging on bad data.
**Trigger:** application start.

1. `StartupUpdateChecker` fetches the static manifest through `HttpUpdateManifestSource`.
2. `UpdateAvailabilityChecker.Evaluate` decides ([03](./03-business-entities-and-rules.md)).
3. On an offer, `AvaloniaUpdatePrompt` shows `UpdateAvailableWindow`; the user downloads, skips — which
   persists that version — or defers.

**Failure behaviour:** an unreachable or malformed manifest produces no prompt at all.
