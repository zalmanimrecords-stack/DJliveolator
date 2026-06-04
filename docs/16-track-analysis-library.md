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

## Phase

Early — buildable in `Liveolator.Core` before the audio-library decision lands (fake
decoder for tests; real decoder swapped in later). Natural first vertical slice: scan a
folder → list tracks with BPM + Camelot key.

## Testing (doc 14)

- Synthetic PCM with a known click-train BPM ⇒ assert detected BPM within tolerance.
- Synthetic tone at a known pitch class ⇒ assert detected key / Camelot code.
- Silence-padded buffer ⇒ assert intro/outro cue positions.
- Corrupt/empty file via fake decoder ⇒ assert `Failed` + scan continues.
- Incremental scan ⇒ unchanged files are not re-analyzed (cache hit by hash).
