# 16 — Track Analysis & Library

> **New module (2026-06-03).** Covers "scan an audio folder and run initial BPM / key /
> scale analysis." Lives in `Liveolator.Core/Analysis` — **pure C#, no UI, no native** —
> so it unit-tests without hardware. Decode is the only impure step and sits behind a seam.

## Purpose

Build the performer's library: walk one or more folders, decode each track, and compute its
**performance metadata offline** — BPM, beatgrid, musical **key + Camelot code**, duration,
and auto-detected **intro/outro cues** — then cache it (doc 13) so live playback, the
harmonic-mixing hint (doc 11), and auto-mix (doc 11) have the data ready instantly.

This is a good **first module** to build: it reuses the frame pipeline (doc 02), beat engine
(doc 03), and key detection (doc 03), is entirely in `Liveolator.Core`, and is fully
testable against synthetic/known PCM without any audio hardware or the open audio-library
decision being resolved.

## Decode seam (keeps Core library-independent)

Analysis needs PCM, but the audio library is an **open decision** (doc 00). The module
depends only on a narrow seam; the real decoder (FFmpeg / the chosen audio library) is
implemented in `Liveolator.Audio` and injected.

```csharp
public interface IAudioDecoder
{
    // Decode a whole file to mono PCM at the target sample rate, for offline analysis.
    // Streamed in blocks so large files don't load entirely into memory.
    IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
        string filePath, int targetSampleRate, CancellationToken ct);

    bool CanDecode(string filePath);   // by extension / probe
}
```

> Tests provide a fake `IAudioDecoder` that yields synthetic PCM (a click train at a known
> BPM, a tone at a known pitch class) so BPM/key results are asserted against ground truth.

## Metadata seam (tags, independent of decode)

Tag/stream metadata (artist, album, genre, year, track #, bitrate, sample-rate, channels,
codec) is read through a second narrow seam so `Core` stays free of file IO and third-party
tag libraries — the same pattern as `IAudioDecoder`:

```csharp
public interface ITrackMetadataReader
{
    // Never throws: an untagged/corrupt file degrades to null (logged), never aborting a scan.
    TrackMetadata? Read(string filePath);
}
```

`TrackMetadata` is an all-nullable record; `MusicTrack.Title` prefers the tag title and falls
back to the file name when untagged. Metadata is captured even for tracks whose audio fails to
decode (tags are still useful). The concrete reader is **ATL.NET** (`z440.atl.core`, MIT — pure
managed, cross-platform) in `Liveolator.Audio` (`AtlMetadataReader`).

## Output model

```csharp
public sealed record TrackAnalysis(
    string FilePath,
    string ContentHash,        // for cache invalidation (see Persistence)
    TimeSpan Duration,
    double Bpm,
    double BpmConfidence,      // 0..1
    BeatGrid Grid,             // offset + bpm (doc 03)
    MusicalKey? Key,           // tonic + mode + Camelot code + confidence (doc 03); null if low-confidence
    TrackCues Cues,            // intro/outro markers (below)
    AnalysisStatus Status);    // Ok | PartiallyAnalyzed | Failed

public sealed record TrackCues(
    TimeSpan? IntroStart, TimeSpan? IntroEnd,
    TimeSpan? OutroStart, TimeSpan? OutroEnd);  // Intro Start / Outro End via silence detection
```

## Analysis pipeline (offline, per track)

```text
file ──IAudioDecoder──> mono PCM blocks
   ├─→ FFT / spectrum (doc 02)
   │      ├─→ OnsetDetectionEngine → TempoEstimator → BeatGrid     (doc 03)  ⇒ Bpm, Grid
   │      └─→ ChromaExtractor → KeyClassifier → MusicalKey         (doc 03)  ⇒ Key (Camelot)
   └─→ Silence/energy envelope → intro/outro cue detection                    ⇒ Cues
```

- **BPM/beatgrid** and **key** reuse the doc 03 components unchanged — analysis is the
  offline driver of the same algorithms the live beat engine uses.
- **Intro/outro cues** via silence detection (Intro Start / Outro End), matching the Mixxx
  Auto DJ model (doc 11); these feed auto-mix transition timing.
- Octave-error handling (½×/2×) is the beat engine's responsibility (doc 03); analysis
  stores the chosen BPM **and** confidence so the UI can flag low-confidence tracks.

## Library scan

```csharp
public interface ITrackLibrary
{
    IAsyncEnumerable<TrackAnalysis> ScanAsync(
        IReadOnlyList<string> folders, IProgress<ScanProgress> progress, CancellationToken ct);

    TrackAnalysis? TryGet(string filePath);                    // from cache
    IReadOnlyList<TrackAnalysis> HarmonicMatches(TrackAnalysis seed);  // Camelot rule (doc 11)
}

public sealed record ScanProgress(int Done, int Total, string CurrentFile);
```

- **Folder walk** over configured roots; filter by `IAudioDecoder.CanDecode`.
- **Incremental:** skip files whose `ContentHash` (or path+size+mtime) already has a cached
  `TrackAnalysis`; only analyze new/changed files.
- **Background & cancellable:** runs off the UI thread, reports `ScanProgress`, and is
  cancellable (closing the app / changing folders mid-scan must not hang or crash).
- **Bounded concurrency:** analyze N tracks in parallel (CPU-bound), tuned to core count.
- `HarmonicMatches` applies the Camelot rules (±1 same letter, or same number switched
  letter — doc 03/11) to suggest compatible next tracks.

## Persistence (doc 13)

- `TrackAnalysis` is cached keyed by **content hash**, so moving/renaming a file does not
  force re-analysis and editing a file invalidates its entry.
- The cache is JSON under the Live persistence root (doc 13); a corrupt/blank cache simply
  triggers re-analysis, never a crash.

## Error handling & logging

- Per-file analysis runs in try/catch: a **corrupt/unsupported file logs a warning, is
  marked `Failed`, and the scan continues** — one bad file never aborts the library scan
  (global standards #16, #26).
- Decode failures surface the file path + reason; never an empty catch, never raw audio in
  logs.
- A track that decodes but yields low-confidence BPM/key is `PartiallyAnalyzed`, not
  `Failed` — the performer can still load it and beat-match manually.

## Relationship to other modules

- **Beat engine (doc 03):** shares the exact onset/tempo/key components; analysis is their
  offline batch driver.
- **Decks (doc 11):** loads `TrackAnalysis` so a freshly loaded deck already knows BPM,
  beatgrid, key, and cues — enabling instant Sync Lock and auto-mix.
- **Playlist/library (doc 09):** the scanned set is the crate the performer loads from.
- **Audio binding (doc 01):** provides the concrete `IAudioDecoder`.

## Implemented design (2026-06-03) — Media Library = shared scan infra + two domains

Per the chosen structure, the library is split into a **music** domain and a **visual media**
domain over a shared, fully unit-tested scan core (all in `Liveolator.Core/Library`):

- **Shared infra:** `IFileEnumerator` (filesystem seam), `IncrementalScan.Diff` (pure
  Added/Modified/Unchanged/Removed classification by size+mtime fingerprint), and the generic
  `MediaLibrary<TEntry>` base — incremental scan (skip unchanged, drop removed), cancellation,
  `IProgress<ScanProgress>`, and **failure isolation** (a bad file becomes a `Failed` entry,
  never aborts the scan; failures are surfaced as queryable entry state, not swallowed).
- **Music:** `MusicLibrary : MediaLibrary<MusicTrack>` — runs offline BPM/key analysis per
  file via `IAudioDecoder` + `TrackAnalyzer`; `HarmonicMatches(seed)` applies the Camelot rules.
- **Visual media (files only):** `VisualMediaLibrary : MediaLibrary<VisualAsset>` — classifies
  images vs video by extension and probes dimensions/duration via the `IVisualMediaProbe` seam;
  `OfKind(kind)` filters. Live camera/capture is a separate runtime source, **not** in this library.

The real bindings live outside Core and stay pure/hardware-free:
- **Decode:** `Liveolator.Audio` — `WavAudioDecoder` (managed) + `FfmpegAudioDecoder` (CLI
  subprocess, compressed formats) behind `CompositeAudioDecoder`.
- **Metadata:** `Liveolator.Audio` — `AtlMetadataReader` (ATL.NET / `z440.atl.core`, MIT)
  implements `ITrackMetadataReader`; injected into `MusicLibrary` so each scanned track carries
  its tags + stream facts alongside the analysis.
- **Filesystem + persistence:** `Liveolator.Media` — `FileSystemFileEnumerator`,
  `JsonCatalogStore` (doc 13 cache; pairs with `MediaLibrary.Restore()` for cache-seeded
  incremental scans), `PlaylistWriter`.
- **Visual probe:** `Liveolator.Visuals` — `CompositeVisualMediaProbe` = `ImageHeaderProbe`
  (managed header reads: PNG/JPEG/GIF/BMP/WebP) + `FfprobeVideoProbe` (video dims/duration via ffprobe).
- **Harmonic sets:** `Liveolator.Core/Playlist/HarmonicSetBuilder` builds Camelot-compatible
  track sequences over the analyzed library.
- **External surface:** `Liveolator.Mcp` exposes library/analysis/harmonic/playlist as MCP tools.

> **Consolidation pending (see doc 00-architecture-overview):** parallel work left duplicate
> `IFileEnumerator` (Platform/Media/App) and `WavAudioDecoder` (Audio/App) implementations.
> Canonical = **Media** (file enumeration) and **Audio** (decode); the redundant copies are
> removed once the running App unlocks its build output.

## Deferred idea — online metadata enrichment (not now)

A future, **optional** enrichment step could augment local analysis with online data
(genre/style, plus a BPM/key **cross-check**). Decisions captured so we don't relearn them:

- **Identify by acoustic fingerprint, not filename** — Chromaprint + AcoustID → MusicBrainz
  (filenames are unreliable). Filename is fallback only.
- **Sources:** MusicBrainz/Discogs (genre/style), Spotify audio-features or Beatport/Tunebat
  (tempo/key/energy cross-check) — **licensing/ToS is the deciding factor for a distributed
  app** and must be researched first (like the audio-stack decision).
- **Architecture:** an `IMetadataProvider` seam, **offline-first** (local analysis always
  works; enrichment only augments, with provenance local-vs-online), cached, rate-limited,
  API keys in config (never hardcoded). Agreement with local detection → raise confidence to
  Ok; disagreement → flag `PartiallyAnalyzed` for review.
- **Status: deferred** (2026-06-03) by user decision; revisit after the core modules/UI.

## Phase

Early — buildable in `Liveolator.Core` before the audio-library decision lands (fake
decoder for tests; real decoder swapped in later). Natural first vertical slice: scan a
folder → list tracks with BPM + Camelot key. **Status: Core scan/catalog logic for both
domains implemented and unit-tested behind seams.**

## Testing (doc 14)

- Synthetic PCM with a known click-train BPM ⇒ assert detected BPM within tolerance.
- Synthetic tone at a known pitch class ⇒ assert detected key / Camelot code.
- Silence-padded buffer ⇒ assert intro/outro cue positions.
- Corrupt/empty file via fake decoder ⇒ assert `Failed` + scan continues.
- Incremental scan ⇒ unchanged files are not re-analyzed (cache hit by hash).
