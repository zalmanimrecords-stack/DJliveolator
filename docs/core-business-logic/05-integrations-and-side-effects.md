# 05 — Integrations and side effects

- **Purpose:** everything the product reaches outside its own process, and everything it writes.
- **Scope:** native bindings, external processes and services, on-disk stores, and the agent-facing interface.
- **Source of truth:** `src/Liveolator.Audio/**`, `.Midi`, `.Visuals`, `.Online`, `.Media`, `src/Liveolator.Mcp/**`.
- **Last validated:** 2026-09-11 (against commit `b809ec7`)
- **Confidence:** High for the adapters present in code; runtime availability is configuration-dependent throughout.
- **Related:** [flows](./04-critical-flows.md) · [permissions and trust](./09-permissions-and-roles.md) · [hotspots](./10-business-logic-hotspots.md)

## External dependencies

| Integration | Direction and trigger | Business purpose | Failure posture |
| --- | --- | --- | --- |
| BASS / ManagedBass | Outbound, continuous while the app runs | Realtime decode, decks, mixing, output, capture | Composition completes without it and the shell shows an audio-engine warning; playback and sync do nothing |
| RtMidi | Bidirectional, on device open | Controller input and LED feedback | Open failures are logged; MIDI stays unavailable |
| OpenGL via Silk.NET | Outbound, on stage open | Renders the visual composition | Needs a compatible context; failures leave the stage dark |
| FFmpeg / ffprobe CLI | Outbound, per file or per frame | Video, camera and offline audio decode and probing | Optional; per-process failures are isolated where implemented |
| Python analysis runtime | Outbound subprocess, on demand | Stem separation and song-structure segmentation | Optional install; the advanced analysis is simply unavailable |
| AcoustID | Outbound HTTPS, per track during enrichment | Audio-fingerprint identification | Needs an API key and the fingerprint helper; failures must not destroy local analysis |
| GetSongBPM-compatible provider | Outbound HTTPS, per track during enrichment | Cross-check tempo and metadata | Needs credentials; a disagreement flags the track rather than overwriting it |
| Update manifest over HTTP | Outbound, once at startup | Decide whether a newer build exists | An unreachable or malformed manifest produces no prompt |
| System browser | Outbound, on user choice | Opens the download URL for an update | Opened through `IUrlOpener`; no in-app download |
| DJ-application libraries | Inbound, on explicit import | Import tracks, cues, grids, keys and playlists from Rekordbox, Serato, Traktor, Mixxx, Engine DJ and VirtualDJ | Import-only; path resolution and merge policy decide what is usable |
| MCP stdio | Inbound, per tool call | Lets an external agent read, analyse and author | Exposes DTOs over selected Core services; it cannot dispatch performance actions |

Auth for the online providers is API keys held in `OnlineSettings`. No secret is documented here; see
[09](./09-permissions-and-roles.md) for how that is handled and where it is weak.

`Needs validation`: none of the HTTP integrations were re-proved in this pass to have an explicit
retry, timeout or idempotency policy. Recorded in [11](./11-open-questions-and-assumptions.md).

## Persistent stores

Rooted at the per-user application-data folder resolved by `JsonCatalogStore.DefaultRoot`
(`%APPDATA%/Liveolator` on Windows, the macOS or XDG equivalent elsewhere):

```text
<app-data>/Liveolator/
  catalog.music.json          music catalog cache (regenerable)
  catalog.visual.json         visual-media catalog cache (regenerable)
  scan-folders.json           the scan roots the user added
  live/
    mappings/<name>.json      ControllerMappingProfile
    scenes/<name>.json        VisualBank and its VisualScenes
    macros.json               VisualMacro definitions
    autopilot/<name>.json     AutopilotRuleSet
  renders/                    offline render output (default location)
```

Track-linked visual programmes are stored separately by `JsonTrackVisualProgramStore`, one versioned
file per track named by a SHA-256 hash of the normalised path, with the full path and file
fingerprint kept inside the programme for validation and relinking. Separated stems
(`StemStore`) and the optional Python environment (`PythonRuntime`) live under *local* application
data, since they are large and regenerable. A SQLite catalog store (`SqliteCatalogStore`) is an
alternative to the JSON catalog.

### Storage rules

- Every file is a versioned snapshot (`{ "Version": N, ... }`) written atomically, temp-then-move.
- Loads are tolerant: a missing file yields null or empty silently; a corrupt or version-mismatched
  file yields null or empty **and** warns, never throws.
- Profile names are sanitised to a flat `<safe-name>.json`, so a name cannot escape its folder.
- Authored data (`live/`, cues, projects, visual programmes) is treated as precious; catalog and
  analysis caches are regenerable and safe to delete.
- App-shipped defaults under `defaults/live/` are never written to. `Needs validation` — the rule is
  stated in `docs/13-data-and-persistence.md`; the directory was not observed in this pass.

## Material side effects on user data

These write, rewrite or delete files outside the application's own state and deserve explicit
confirmation at every entry point:

- **Recording** (`MasterRecordToggle` → `IMasterRecorder`) writes a WAV capture of the post-limiter
  master.
- **Offline render** writes a mixed-down file from a `MixPlan`, and the streaming continuous-mix
  export writes a whole set to one file.
- **Library repair and relocation** (`LibraryDoctor`, `LibraryReferenceRewriter`) can rewrite catalog
  references and, where explicitly requested, remove files.
- **Extension installation** writes package content plus registry and trust state.
- **Library import** writes tracks, cues, grids and playlists into the existing stores.

`Needs validation`: whether every entry point to library repair requires a preview and confirmation
before a destructive step. Item in [11](./11-open-questions-and-assumptions.md), improvement in
[14](./14-final-improvement-report.md).

## Agent surface

`Liveolator.Mcp` exposes **30** attributed tools over stdio (`src/Liveolator.Mcp/Tools/*.cs`),
grouped as:

| Group | Tools |
| --- | --- |
| Library | `scan_music_folders`, `list_tracks`, `get_track`, `get_catalog_stats`, `reanalyze_track`, `reanalyze_pending_tracks`, `set_track_analysis`, `import_library` |
| Search | `find_tracks` |
| Analysis | `analyze_track`, `measure_catalog_loudness` |
| Harmonic | `harmonic_matches`, `compatible_keys` |
| Playlist | `build_harmonic_playlist`, `export_playlist` |
| DJ sets | `build_dj_set`, `get_dj_set`, `list_dj_sets`, `render_set_preview`, `export_set_mix` |
| Enrichment | `lookup_track_online` |
| Visuals | `scan_visual_folders`, `list_visuals`, `get_visual`, `get_visual_preset_spec`, `create_visual_preset`, `list_visual_presets` |
| Control skins | `get_control_skin_spec`, `create_control_skin`, `list_control_skins` |

The DJ-set group is the only one that writes a `StudioProject` and renders audio; the flow and its
gates are in [04](./04-critical-flows.md). A connection guide for agent authors lives at
[`docs/mcp-connect-guide.md`](../mcp-connect-guide.md).

**A child process must never inherit the server's stdin.** `FfmpegAudioDecoder` handed the spawned
ffmpeg the stdio server's JSON-RPC stdin, which deadlocked `scan_music_folders` at 0% CPU. Fixed in
`3611ad6`; the App's own library scan was never affected.
