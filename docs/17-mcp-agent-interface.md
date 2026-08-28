# 17 — MCP Agent Interface

> **New module (2026-06-04).** An [MCP](https://modelcontextprotocol.io) server that exposes
> Liveolator's music intelligence to external AI agents (Claude Desktop/Code and any MCP
> client). It lets an agent read music folders, analyze and explore the collection, get
> harmonic-mixing suggestions, and build playlists from that research.

## Purpose

Liveolator's architecture (doc 00) is built so every *source of intent* drives the app through
serializable commands. An MCP server is exactly such a source: it lets an outside agent reach
in. This first cut targets the part of the product that is **real and pure today** — the
Track-Analysis / library engine (doc 16) — rather than live performance control, which is not
yet implemented.

## Scope

In scope (implemented):

- **Scan music folders** (recursively), analyze each track offline (BPM, musical key + Camelot
  code, intro/outro cues, duration), and **cache** the result so later calls are instant.
- **Explore** the catalog: list/filter/sort tracks, fetch one track, aggregate stats.
- **Inspect rich tags and analysis provenance**: artist/album/genre/year/stream facts, track vs
  sample classification, analyzer version, and whether a result was manually corrected.
- **Repair analysis**: force one catalogued track through local analysis again, or resume all
  failed/incomplete/stale entries while preserving manual corrections.
- **Analyze** a single file on demand without cataloging it.
- **Harmonic mixing**: compatible-key lookup (Camelot) and harmonically-compatible matches for a
  seed track.
- **Playlists**: build a harmonically-coherent set from a seed (with a tempo trend) and export
  it to `.m3u8` / `.json`.
- **Visual-asset catalog**: scan folders of images/video clips (dimensions, and video duration
  via ffprobe), then list/query them so an agent can discover footage to pair with music.

Out of scope (documented for later):

- **Live performance control** (transport / deck / mixer / visual). It needs the
  `PerformanceAction` dispatcher + engines (doc 04). When those land, the dispatcher's
  serializable actions become additional MCP tools — the agent becomes another action source
  alongside Push, the DJ controller, the UI, and autopilot.
- **Generating** visual content. Needs the visual engine (doc 08). The MCP server only catalogs
  and lists *existing* visual files; choosing/creating material is left to the agent.

## Project layout

Honors doc 00: `Liveolator.Core` stays pure; concrete IO lives in bindings; the MCP server is the
composition root.

```text
src/Liveolator.Audio/   # IAudioDecoder impls (doc 01/16): WavAudioDecoder (pure managed),
                        # FfmpegAudioDecoder (FFmpeg CLI), CompositeAudioDecoder (routes .wav→managed)
src/Liveolator.Media/   # FileSystemFileEnumerator (IFileEnumerator), JsonCatalogStore (doc 13
                        # cache), PlaylistWriter (.m3u8/.json)
src/Liveolator.Mcp/     # MCP server: ServerConfig, LibrarySession, tool classes, stdio+HTTP host
src/Liveolator.Core/Playlist/   # pure HarmonicSetBuilder + HarmonicSet model (doc 09 subset)
```

`Liveolator.Core` gained one persistence affordance: `MediaLibrary.Restore(entries)` re-seeds the
catalog from the cache so a following scan only re-analyzes new/changed files.

## Transports

One server, two transports (chosen by argument):

- `--stdio` (default) — stdin/stdout, for a locally-launched agent. **All logs go to stderr** so
  stdout carries only the JSON-RPC protocol.
- `--http [--port N]` — HTTP/SSE on loopback (`127.0.0.1`, default port 5174), for
  remote/already-running agents.

Other flags: `--ffmpeg PATH` (FFmpeg executable; also `LIVEOLATOR_FFMPEG_PATH`), `--data DIR`
(catalog-cache root; also `LIVEOLATOR_DATA`, default `%APPDATA%/Liveolator`).

## Tools

| Tool | What it does |
|------|--------------|
| `scan_music_folders(folders[], force?)` | Scan + analyze + cache **only the folders passed**; returns status counts, elapsed, the folders walked, the folders otherwise known, and any failures. |
| `list_tracks(text?, kind?, artist?, genre?, status?, minBpm?, maxBpm?, camelot?, year?, fileType?, minDurationSeconds?, sort?, descending?, limit?, offset?)` | Rich catalog query over shared Core filter/sort logic. |
| `get_track(path)` | Full analysis for one catalogued track. |
| `get_catalog_stats()` | Counts by status, average BPM, key distribution, 10-BPM histogram. |
| `reanalyze_track(path, force?)` | Refresh one catalogued track and persist it. |
| `reanalyze_pending_tracks()` | Resume all failed/incomplete/old-version analysis; preserve manual edits. |
| `set_track_analysis(path, bpm?, key?)` | Correct a track's tempo and/or Camelot key by hand and lock it against automatic re-analysis; an omitted value keeps what analysis found. |
| `analyze_track(path)` | One-off analysis without cataloging. |
| `harmonic_matches(path, limit?)` | Camelot-compatible tracks for a seed, with the relationship. |
| `compatible_keys(camelot)` | The Camelot keys that mix with a given code (pure theory). |
| `build_harmonic_playlist(seedPath, length, bpmTolerance?, trend?)` | Greedy harmonic set with a tempo trend (Any/Steady/Rising/Falling). |
| `export_playlist(trackPaths[], format, outputPath)` | Write `.m3u8` / `.json`. |
| `scan_visual_folders(folders[], force?)` | Scan + catalog images/videos (dimensions, video duration via ffprobe). |
| `list_visuals(kind?, minWidth?, limit?, offset?)` | Query the visual catalog. |
| `get_visual(path)` | Metadata for one catalogued visual asset. |
| `build_dj_set(seedPath?, trackPaths[]?, length?, bpmTolerance?, trend?, overlapBars?, maxWarpPercent?, excludeLowGridConfidence?, name?)` | Build a beat-matched set and save it as a STUDIO arrangement; returns every transition and every rejected candidate. `trackPaths` restricts the candidate pool to exactly those catalogued tracks and defaults `length` to their count. |
| `list_dj_sets()` | Names of the saved sets. |
| `get_dj_set(name)` | A saved set read back: tempo, tracks in play order with their stretch, and where they overlap. |
| `render_set_preview(name, outputDirectory, sampleRate?)` | Render each transition to its own WAV, with a phrase of lead-in and lead-out. |

Each tool returns a stable DTO (`Liveolator.Mcp.Contracts`) decoupled from Core records, and
surfaces failures as clear errors — never a silent gap (global standards #16, #23, #26).

## DJ set building

An agent builds a whole set through `build_dj_set`. All the mixing decisions live in
`Liveolator.Core/Studio/Set` (`DjSetArranger` + `SetTransitionPlanner`); the tool layer only supplies the
catalog, saves the arrangement, and reports.

- **The pool is the whole catalog unless you name it.** Tempo and key are the only signals the arranger
  orders on, so a second, unrelated library in the same data root competes for every join and wins its
  share — with `rejectedCount: 0`, because from the arranger's side nothing went wrong. `trackPaths`
  restricts the candidates to exactly the records asked for; the arranger still decides their order. A
  seed outside that list is an error rather than a silently widened pool.
- **One tempo per set.** The renderer samples a clip's warp factor once, at the clip's start, so a tempo
  that moves inside a clip is silently not rendered — and two overlapping clips at different rates drift
  apart within a bar. The set tempo is `tempoBpm` when given, otherwise the median of the chosen tracks —
  and the median is a default, not a rule: it is derived from the selection, so a pool weighted toward one
  tempo pins the set there no matter what the room wants. Either way, anything that would stretch past
  `maxWarpPercent` (default 6%) is rejected and reported with the stretch it would have needed. Stepped
  tempo across a long set (tempo changes only at clip boundaries, with the boundary clip split in two)
  is the known next step, not built.
- **The grid can be nudged by hand.** `set_track_analysis(downbeatOffsetMs:)` shifts a track's beat grid
  without re-running analysis, for the case where two records beat-match but their kicks flam. It is
  deliberately separate from the detected anchor: re-analysis recomputes the anchor from scratch and can
  land on a worse one, and it would invalidate the grid-confidence signals the Sync gate reads. Finding
  the right amount is still the DJ's ear — nothing measures it automatically yet.
- **Phrase alignment, by construction.** Every clip enters on one of its own 16-bar phrase lines and
  starts on a project phrase line. Warping to a common tempo maps a track's phrase onto the project's
  phrase exactly, so both hold by induction from the first clip at t=0 — no per-transition correction.
- **Structure is used, never trusted blindly.** Mix points come from `SongStructure` (leave on an outro
  after the last drop, enter where the kick actually starts) but only past four gates: at least three
  sections, real labels, boundaries within a beat of the bar grid, and a chosen point inside the last
  30% of the record. Anything failing them falls back to a distance-from-the-tail rule and says so.
- **Grid confidence gates the stretch.** A track failing `GridConfidenceCalculator` plays at its native
  rate with the shortest legal blend rather than being warped by a ratio derived from a guessed tempo.
- **Every join is mixed, not just overlapped.** An equal-power crossfade (a linear one dips 3 dB in the
  middle of every transition) plus a bass swap across the blend, as `DeckGain`/`EqLow` automation lanes.
- **Previews only.** `render_set_preview` renders the joins, not the set: the offline renderer holds the
  whole master and every decoded source in memory at once, which an hour-long mix does not survive.
  `ProjectSlice` cuts each window out so only the two records involved are decoded.

Rendering needs the native BASS libraries beside the server — every clip in a built set is warped, so the
mix runs through BASS_FX. `src/Bass.Native.targets` ships them to the app, the MCP server, and the MCP
tests from one place.

## Decode / FFmpeg requirement

- **WAV** decodes with the pure-managed `WavAudioDecoder` — no external dependency.
- **mp3/flac/m4a/aac/ogg/opus** are decoded via the **FFmpeg CLI**. FFmpeg must be installed and
  on PATH (or pass `--ffmpeg` / `LIVEOLATOR_FFMPEG_PATH`). When it is absent, those files appear
  under a scan's `failures` with an actionable message; WAV still analyzes.
- This resolves the **offline-decode** half of the open audio-library decision (doc 00/01); the
  **realtime playback** library remains open.

## Persistence

The analyzed catalog is cached as JSON (`catalog.music.json`) under the data root. Paths are
canonicalized (full path) so the same file never gets two spellings, keeping the incremental
cache stable. A missing/corrupt cache logs a warning and triggers a fresh scan — never a crash.

## Testing

`Liveolator.Audio.Tests`, `Liveolator.Media.Tests`, and `Liveolator.Core.Tests/Playlist` cover the
pure pieces (WAV decode, filesystem enumeration + canonical paths, catalog round-trip, playlist
building, restore-then-skip). The MCP tool layer is thin orchestration over these and is verified
by an end-to-end stdio handshake (`initialize` → `tools/list` → `tools/call`).

## Persistence — visual catalog

The visual catalog is cached separately (`catalog.visual.json`) from the music catalog, with the
same atomic-write + incremental-scan behavior. Video duration is populated only when `ffprobe` is
available (`LIVEOLATOR_FFPROBE_PATH`/PATH); images need no external tool.

## Phase

Built after Track-Analysis (doc 16): music intelligence + harmonic playlists + visual-asset
cataloging. Natural next step: performance-control tools once the dispatcher (doc 04) exists —
the agent then becomes another `PerformanceAction` source.
