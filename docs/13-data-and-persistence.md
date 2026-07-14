# 13 — Data and Persistence

## Purpose

Define the persisted data types for Live Mode and the rules for storing them, so
mappings, scenes, and shows survive restarts and can be shared — without clobbering
the user's manual edits.

## Existing persistence this follows

- `MilkDropVisualizer.App/Helpers/Settings.cs` — JSON settings in
  `%LOCALAPPDATA%/MilkDropVisualizer/settings.json`, loaded on demand, debounced
  saves (~250 ms).
- `SessionStateService` — persists window geometry, last preset, playlist, playback
  position, overlay state on close/save.
- `SessionProfileManager` — existing project/export profiles (distinct from live
  show sessions; do not overload it).

Live Mode reuses the same JSON-under-`%LOCALAPPDATA%` approach and the debounced-save
discipline, but stores its data in dedicated files so live data is separable and
shareable.

## Persisted types

| Type | Doc | Contents |
|------|-----|----------|
| `ControllerMappingProfile` | 05 | device hint + bindings |
| `PushProfile` | 06 | Push mapping + feedback config (a specialized mapping profile) |
| `DjControllerProfile` | 07 | DJ controller mapping (a specialized mapping profile) |
| `VisualScene` | 08 | preset(s), overlays, macro values, transition, beat behavior |
| `VisualBank` | 08 | named group of scenes |
| `VisualMacro` | 08 | macro definitions (name, range, target) |
| `BeatGrid` | 03 | per-track beat grid / downbeat anchor (manual edits sacred) |
| `TrackAnalysisCache` | 09 | BPM, beatgrid, waveform, key, energy per track |
| `LivePerformanceSession` | — | active source, loaded profiles, queue, active bank |
| `AutopilotRuleSet` | 10 | rules + scene pool + seed |

## Storage layout

Rooted at the per-user app-data folder (`%APPDATA%/Liveolator` on Windows, the Mac/XDG
equivalent elsewhere — see `JsonCatalogStore.DefaultRoot`):

```text
<app-data>/Liveolator/
  catalog.music.json            # music catalog cache (JsonCatalogStore, regenerable)        [implemented]
  catalog.visual.json           # visual-media catalog cache (JsonCatalogStore, regenerable)
  scan-folders.json             # scan-folder roots the user added (JsonCatalogStore)        [implemented]
  live/
    mappings/<name>.json        # ControllerMappingProfile / Push / DJ profiles      [implemented]
    scenes/<name>.json          # VisualBank (contains its VisualScenes)             [implemented]
    macros.json                 # VisualMacro definitions                            [implemented]
    track-visuals/<hash>.json   # authored per-track image/video timeline             [implemented]
    autopilot/<name>.json       # AutopilotRuleSet                                   [implemented]
    sessions/<name>.json        # LivePerformanceSession (setlists/shows)            [planned]
    cache/track-analysis.json   # TrackAnalysisCache (regenerable)                   [planned]
  defaults/live/                # app-shipped defaults (read-only baseline)          [planned]
```

The four `[implemented]` families are persisted by `LiveProfileStore`
(`src/Liveolator.Media`), behind the `ILiveProfileStore` Core seam
(`src/Liveolator.Core/Persistence`). Each file is a versioned snapshot
(`{ "Version": N, ... }`) saved atomically (temp-then-move). Loads are tolerant: a
missing file returns null/empty with no warning; a corrupt or older-version file returns
null/empty **and** reports a warning, never throwing (global standards #16/#26). Profile
names are sanitized to a flat `<safe-name>.json` so a name can never escape its folder.

Track-linked visual programs are persisted separately by `JsonTrackVisualProgramStore`,
behind the Core `ITrackVisualProgramStore` seam. Each track has one versioned file whose
name is a SHA-256 hash of its normalized path; the full path and file fingerprint remain
inside the authored program for validation and future relinking. Saves are serialized and
atomic, while corrupt or incompatible files are ignored with a warning.

## Persistence rules (from the plan)

1. **User data separate from app defaults.** Shipped defaults (Push v1 profile,
   starter scenes) live under `defaults/live/` and are never written to. User edits
   are clones under `live/` (global standard #20 — don't break the baseline).
2. **Mappings exportable / importable.** Profiles are self-contained JSON for sharing
   (docs 05–07).
3. **Do not overwrite manual beatgrid edits during automatic reanalysis** unless the
   user explicitly requests it. A `BeatGrid` carries an `IsManual` flag; reanalysis
   skips manual grids.
4. **Regenerable vs authored.** `TrackAnalysisCache` is a cache (safe to delete);
   scenes/mappings/sessions are authored data (treated as precious).

## Versioning & migration

Each file carries a `schemaVersion`. Loaders tolerate older versions and migrate
forward; unknown newer versions load defensively (don't crash, log, fall back). This
follows the safe-migration spirit of global standard #22 for file-based data.

## Validation (global standard #19)

- All loaded JSON is validated: referenced preset paths, slot ranges, action kinds,
  device hints. Invalid entries are dropped with a logged warning, not silently
  accepted, and never crash startup.
- Import of a shared profile is validated the same way before it is applied.

## Error handling & logging

- Load/save wrapped in try/catch with the file path in context; a corrupt file is
  backed up (`.bak`) and a default is used, so a bad file never blocks the app
  (global standards #16, #26).
- Never log file contents that could include user-identifying paths beyond what is
  necessary for diagnosis.

## Phase

Cross-cutting. Each persisted type lands with its owning subsystem's phase; the
`live/` layout, versioning, and the defaults-vs-user separation are established when
the first profile is saved (Phase 5).

## Risks

- Beat-grid protection (#3) is easy to get wrong; the `IsManual` flag and a test that
  asserts reanalysis skips manual grids are mandatory (doc 14).
- Schema churn across phases — keep types additive and versioned to avoid breaking
  saved shows.
