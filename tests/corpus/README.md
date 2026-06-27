# Real-audio beat-detection corpus

Drop real tracks here with hand-annotated ground truth to measure the offline beat detector
(`BpmDetector` / `TrackAnalyzer`) against reality. This is the gating measurement the system review
(2026-06-27) called for: the synthetic corpus in `tests/Liveolator.Core.Tests/Analysis/Corpus` proves the
phase-anchor + HPSS gains in principle, but only real material reveals how the detector behaves on the
actual library — and lets us decide objectively whether the cheap HPSS is enough or a Demucs stem is worth
its weight.

## How to use

1. Copy a few audio files (WAV / FLAC / MP3 — anything FFmpeg can decode) into this folder, or into a
   folder pointed to by the `LIVEOLATOR_CORPUS_DIR` environment variable.
2. Create `annotations.json` next to them (see `annotations.sample.json`):

   ```json
   [
     { "file": "track-a.wav", "bpm": 128.0, "firstBeatSeconds": 0.043 },
     { "file": "track-b.flac", "bpm": 174.0, "firstBeatSeconds": 0.210 }
   ]
   ```

   - `bpm` — the true tempo. Scored octave-aware (a half/double detection is flagged, not silently passed).
   - `firstBeatSeconds` — the time of any clear kick/down-beat near the start. The detector's first-beat
     anchor is compared **circularly** (modulo one beat), so any beat works — you do not need beat 1 of bar 1.
   - `downbeatSeconds` *(optional)* — if set, the bar (down-beat) anchor is scored too.

3. Run: `dotnet test tests/Liveolator.Integration.Tests --filter RealAudioBeatCorpus`
   - With no `annotations.json` present the test is a no-op (zero cases), so CI stays green.
   - The run prints a per-track report (detected vs truth, phase error in ms) and asserts each track is
     within tolerance (`bpm` ±3 octave-aware, phase < 50 ms). Tune tolerances in
     `RealAudioBeatCorpusTests` once you have a feel for your material.

## Picking tracks

Choose the ones where SYNC felt off, plus a spread of tempos/genres (house, techno, trance, **DnB ~174** —
the synthetic corpus already shows fast tempos are the weak spot). Files in this folder are git-ignored.
