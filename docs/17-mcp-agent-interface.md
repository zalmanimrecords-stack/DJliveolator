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
| `scan_music_folders(folders[], force?)` | Scan + analyze + cache; returns status counts, elapsed, and any failures. |
| `list_tracks(status?, minBpm?, maxBpm?, camelot?, sort?, limit?, offset?)` | Query the catalog. |
| `get_track(path)` | Full analysis for one catalogued track. |
| `get_catalog_stats()` | Counts by status, average BPM, key distribution, 10-BPM histogram. |
| `analyze_track(path)` | One-off analysis without cataloging. |
| `harmonic_matches(path, limit?)` | Camelot-compatible tracks for a seed, with the relationship. |
| `compatible_keys(camelot)` | The Camelot keys that mix with a given code (pure theory). |
| `build_harmonic_playlist(seedPath, length, bpmTolerance?, trend?)` | Greedy harmonic set with a tempo trend (Any/Steady/Rising/Falling). |
| `export_playlist(trackPaths[], format, outputPath)` | Write `.m3u8` / `.json`. |
| `scan_visual_folders(folders[], force?)` | Scan + catalog images/videos (dimensions, video duration via ffprobe). |
| `list_visuals(kind?, minWidth?, limit?, offset?)` | Query the visual catalog. |
| `get_visual(path)` | Metadata for one catalogued visual asset. |

Each tool returns a stable DTO (`Liveolator.Mcp.Contracts`) decoupled from Core records, and
surfaces failures as clear errors — never a silent gap (global standards #16, #23, #26).

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
