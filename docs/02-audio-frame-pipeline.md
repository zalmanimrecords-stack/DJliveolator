# 02 — Audio Frame Pipeline

## Purpose

Make the existing visualization and analysis source-agnostic. The pipeline turns
raw samples from any `IAudioSource` (doc 01) into the two outputs the rest of the
app needs: PCM for projectM and analysis frames for the beat engine.

## Existing code this touches

- `MilkDropVisualizer.App/Audio/AudioAnalyzer.cs` — owns the current 2048-point FFT
  (Hamming window), spectrum, waveform downsample, and 512-sample PCM chunk that is
  pushed to projectM. Its computation is reused; its *input* changes from
  "re-read the file stream" to "read from the ring buffer / latest samples."
- `MilkDropVisualizer.App/Visualization/ProjectMVisualizerHost.xaml.cs` —
  consumer; `projectm_pcm_add_float` is fed 512 floats per frame today.
- `MilkDropVisualizer.App/Audio/BpmDetector.cs` — current consumer of the bass band
  of the FFT; will be replaced by the beat engine (doc 03) consuming the same
  spectrum.

## Proposed interfaces

```csharp
public interface IAudioFrameProvider
{
    AudioFrameData GetLatestFrame();
    event EventHandler<AudioFrameData>? FrameAvailable;
}

public sealed record AudioFrameData(
    float[] MonoPcm,        // mono float PCM for analysis + projectM (512+)
    float[] Spectrum,       // magnitude spectrum (FFT)
    float[] Waveform,       // downsampled waveform for UI/overlays
    int SampleRate,
    long FrameIndex,        // monotonically increasing
    double TimestampSeconds);
```

Immutable per-frame snapshot (record) so consumers on other threads read a
consistent picture without locking (doc 00 threading model).

## Responsibilities (in order)

1. **Pull** samples from the active source's ring buffer at the analysis cadence.
2. **Resample** to the analysis sample rate when the source rate differs (loopback
   may be 44.1/48/96 kHz). A simple linear/polyphase resampler is sufficient for
   spectral analysis.
3. **Downmix to mono** for analysis (average channels). Keep an interleaved/stereo
   path available where projectM or future routing needs it.
4. **Compute FFT/spectrum** — reuse `AudioAnalyzer`'s existing windowed FFT.
5. **Emit `AudioFrameData`** — raise `FrameAvailable` and update the latest-frame
   snapshot.

## Refactor strategy (preserve behavior)

`AudioAnalyzer` currently couples *reading the file* with *computing the FFT*.
Split it (single responsibility, global standard #3):

- `SpectrumAnalyzer` — pure: `float[] mono -> (spectrum, waveform)`. Extracted from
  the existing FFT code, unchanged numerically.
- `AudioFramePipeline : IAudioFrameProvider` — orchestrates pull → resample →
  downmix → `SpectrumAnalyzer` → emit.

With Live Mode off, the pipeline's source is `DeckAudioSource`, and the numbers
flowing into projectM are identical to today — this is a behavior-preserving
refactor (global standard #7) and must be covered by a regression test that feeds a
known buffer and asserts the spectrum matches the pre-refactor output.

## projectM feed

projectM still receives a 512-sample mono PCM chunk per render frame via
`projectm_pcm_add_float`. The only change: the chunk comes from
`AudioFramePipeline.GetLatestFrame().MonoPcm` instead of an on-demand file read.
The OpenGL/interop path in `ProjectMVisualizerHost` is unchanged.

## Beat engine feed

The beat engine (doc 03) subscribes to `FrameAvailable` and consumes
`Spectrum`/`MonoPcm` for onset detection. It does **not** re-read audio; it shares
the same frames as projectM, guaranteeing visuals and beat analysis see identical
audio.

## Cadence & latency

- The pipeline runs on a timer/loop independent of the render loop so a slow frame
  never stalls capture.
- Target end-to-end (capture → frame available) latency is a measured metric
  (doc 14). Keep the analysis hop small enough for responsive beat tracking but
  large enough to avoid CPU thrash (e.g. 512–1024 sample hops at 44.1 kHz).

## Error handling & logging

- Guard resample/FFT against zero-length or malformed buffers; on a bad frame, emit
  the previous valid frame rather than throwing into the render loop.
- Log source-rate / format changes once (not per frame) to avoid log spam.

## Phase

Phase 1 (pipeline + projectM feed) with the `SpectrumAnalyzer` extraction.
The beat-engine subscription lands in Phase 2.

## Risks

- Resampling artifacts can bias the spectrum; validate against the synthetic click
  tracks (doc 14) at multiple sample rates.
- The extraction must be numerically faithful — the regression test is mandatory
  before deleting the old code path (global standard #6, #8).
