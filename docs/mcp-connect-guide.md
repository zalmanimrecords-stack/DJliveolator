# Connecting to the Liveolator MCP server

> **Audience: another AI agent.** This is the *how to connect* guide. What each tool means and why
> the DJ-set builder behaves the way it does lives in [doc 17](17-mcp-agent-interface.md) — read that
> once you are connected.

Liveolator ships its own MCP server (`src/Liveolator.Mcp` → `liveolator-mcp.dll`, .NET 8). It exposes
**27 tools** over the music/visual catalog: scan and analyze tracks, query the library, harmonic
mixing, playlists, DJ-set building and preview rendering, DJ-library import, and visual-preset /
control-skin authoring.

It does **not** control live playback. There is no transport/deck/mixer tool — those need the
`PerformanceAction` dispatcher and are explicitly out of scope today.

---

## 1. Prerequisites

| Need | Why | If missing |
|------|-----|------------|
| .NET 8 SDK (or runtime) | Runs the server | Nothing works |
| **FFmpeg** on `PATH` | Decodes mp3/flac/m4a/aac/ogg/opus | Those files land in a scan's `failures`; WAV still analyzes |
| `ffprobe` on `PATH` | Video duration in the visual catalog | Video assets get no duration; images are fine |
| BASS natives beside the DLL | `render_set_preview` warps every clip through BASS_FX | The build copies `bass.dll`, `bass_fx.dll`, `bassmix.dll`, `bassflac.dll`, `bass_aac.dll` automatically — do not hand-copy |

## 2. Build it first

The server is launched as a built DLL, not via `dotnet run`. Build before the first connection and
after any code change:

```bash
dotnet build src/Liveolator.Mcp/Liveolator.Mcp.csproj
```

Output: `src/Liveolator.Mcp/bin/Debug/net8.0/liveolator-mcp.dll`

## 3. Connect

### Claude Code — already wired in this repo

The repo root has a project-scoped [`.mcp.json`](../.mcp.json):

```json
{
  "mcpServers": {
    "liveolator": {
      "command": "dotnet",
      "args": ["src/Liveolator.Mcp/bin/Debug/net8.0/liveolator-mcp.dll", "--stdio"]
    }
  }
}
```

Start Claude Code with the repo as its working directory and the tools appear as
`mcp__liveolator__<tool>`. **The path is relative** — if your working directory is not the repo root,
replace it with the absolute path:
`C:\Users\SimonRosenfeld\DEV\Liveolator\src\Liveolator.Mcp\bin\Debug\net8.0\liveolator-mcp.dll`.

Registering it yourself from the CLI:

```bash
claude mcp add liveolator -- dotnet C:/Users/SimonRosenfeld/DEV/Liveolator/src/Liveolator.Mcp/bin/Debug/net8.0/liveolator-mcp.dll --stdio
```

### Claude Desktop / any other stdio client

Add the same block to the client's config (Claude Desktop on Windows:
`%APPDATA%\Claude\claude_desktop_config.json`), using **absolute paths**, then restart the client.

### HTTP instead of stdio

For an agent that is already running and cannot spawn a child process:

```bash
dotnet src/Liveolator.Mcp/bin/Debug/net8.0/liveolator-mcp.dll --http --port 5174
```

Serves the MCP endpoint at `http://127.0.0.1:5174` — **loopback only**, by design; it is not
reachable from another machine. Default port is 5174.

## 4. Launch flags and environment

Every flag has an environment-variable equivalent, so a client that only lets you set `env` can still
configure the server.

| Flag | Env var | Default / effect |
|------|---------|------------------|
| `--stdio` | — | Default transport. All logs go to **stderr** so stdout stays pure JSON-RPC |
| `--http` / `--port N` | — | HTTP/SSE on `127.0.0.1:N`, default 5174 |
| `--ffmpeg PATH` | `LIVEOLATOR_FFMPEG_PATH` | FFmpeg executable; otherwise resolved from `PATH` |
| `--data DIR` | `LIVEOLATOR_DATA` | Catalog root. Default `%APPDATA%\Liveolator` |
| `--getsongbpm-key KEY` | `LIVEOLATOR_GETSONGBPM_KEY` | Enables `lookup_track_online`. Without it the tool resolves but reports "not configured" |
| `--acoustid-key KEY` | `LIVEOLATOR_ACOUSTID_KEY` | Fingerprint matching; without it lookup matches by tags only |
| `--fpcalc PATH` | `LIVEOLATOR_FPCALC_PATH` | Chromaprint `fpcalc`; degrades to null when absent |
| — | `LIVEOLATOR_FFPROBE_PATH` | ffprobe, resolved separately from ffmpeg |

An unknown argument is fatal and prints the valid set to stderr — a bad launch fails fast rather than
starting half-configured.

## 5. What you are sharing with the running app

**The MCP server and the desktop app write the same SQLite catalog** under the data root
(`%APPDATA%\Liveolator` unless `--data` says otherwise). That is deliberate — an agent's scan shows
up in the app's LIBRARIES screen — but it means:

- Your `scan_music_folders` / `reanalyze_*` calls mutate the owner's real library.
- Writes are per-row, so the app and the server do not clobber each other's rows.
- A legacy JSON catalog is migrated to SQLite once, automatically, on first start.
- Hot cues, playlists and STUDIO projects persist to the same shared folders.

Point `--data` at a scratch directory if you want an isolated catalog to experiment in.

## 6. Verify the connection

If your client reports tools, you are done. To check the server directly, drive a raw stdio
handshake — `initialize` → `notifications/initialized` → `tools/list`, one JSON object per line.

**Gotcha:** piping a file straight into stdin (`server --stdio < handshake.jsonl`) returns *nothing* —
stdin hits EOF and the host shuts down before the responses flush. Keep stdin open for a few seconds:

```powershell
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "dotnet"; $psi.Arguments = "src\Liveolator.Mcp\bin\Debug\net8.0\liveolator-mcp.dll --stdio"
$psi.WorkingDirectory = "C:\Users\SimonRosenfeld\DEV\Liveolator"
$psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.UseShellExecute = $false
$p = [System.Diagnostics.Process]::Start($psi)
Get-Content handshake.jsonl | ForEach-Object { $p.StandardInput.WriteLine($_) }
$p.StandardInput.Flush(); Start-Sleep -Seconds 5; $p.StandardInput.Close()
$p.StandardOutput.ReadToEnd()
```

A healthy server answers with `"serverInfo":{"name":"liveolator-mcp"...}` and then 27 tools.

## 7. The 27 tools

Semantics, parameters and the DJ-set rules are in [doc 17](17-mcp-agent-interface.md). The names:

- **Library** — `scan_music_folders`, `list_tracks`, `find_tracks`, `get_track`, `get_catalog_stats`,
  `reanalyze_track`, `reanalyze_pending_tracks`, `import_library`
- **Analysis / enrichment** — `analyze_track`, `lookup_track_online`
- **Harmonic** — `harmonic_matches`, `compatible_keys`
- **Playlists** — `build_harmonic_playlist`, `export_playlist`
- **DJ sets** — `build_dj_set`, `list_dj_sets`, `get_dj_set`, `render_set_preview`
- **Visuals** — `scan_visual_folders`, `list_visuals`, `get_visual`, `get_visual_preset_spec`,
  `create_visual_preset`, `list_visual_presets`
- **Control skins** — `get_control_skin_spec`, `create_control_skin`, `list_control_skins`

`list_tracks` filters and sorts; `find_tracks` is the free-text search. Run `scan_music_folders`
before anything else if the catalog is empty.

## 8. When it does not work

| Symptom | Cause |
|---------|-------|
| Client shows no tools | DLL not built, or the path in the config is wrong / relative to the wrong cwd |
| Every non-WAV file fails a scan | FFmpeg not on `PATH` — pass `--ffmpeg` |
| `lookup_track_online` says "not configured" | No GetSongBPM key supplied |
| `render_set_preview` fails to load native audio | BASS natives missing beside the DLL — rebuild rather than copying by hand |
| Catalog looks empty | Wrong `--data` root, or `scan_music_folders` was never run |
| Protocol garbage on stdout | Something wrote to stdout instead of stderr — a bug, report it; stdio mode requires a clean stdout |
