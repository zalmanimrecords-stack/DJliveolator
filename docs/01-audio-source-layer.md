# 01 — Audio Source Layer

> **Revision status (doc 00):** this doc still shows the Zalmanolator-era Windows stack
> (NAudio types, `WasapiLoopbackCapture`, `AsioOut`). Per doc 00 the project is now
> cross-platform and the **audio library is an OPEN DECISION** (BASS/ManagedBass vs
> PortAudio/miniaudio). Treat the NAudio-specific types below as **placeholders**: the
> `IAudioSource` seam and the contracts are what carry over; the concrete backend
> (Windows: ASIO/WDM-KS/WASAPI · macOS: CoreAudio) is bound once the library is chosen.
> The **latency targets and real-time-thread rules** added below are library-independent
> and apply regardless.

## Purpose

Provide a normalized stream of audio frames regardless of origin, so the
visualizer and beat engine no longer assume "audio == a file being played."

## Existing code this touches

- `MilkDropVisualizer.App/Audio/AudioPlayer.cs` — NAudio `WaveOutEvent`/`WasapiOut`
  file playback. Stays as the deck output; wrapped, not replaced.
- `MilkDropVisualizer.App/Audio/AudioAnalyzer.cs` — currently re-reads the file
  stream on demand to compute FFT/PCM. This becomes one *consumer* of the new
  frame pipeline rather than the sole audio entry point.
- `MilkDropVisualizer.App/Audio/PlaylistAudioPlayer.cs` — playlist wrapper over
  `AudioPlayer`; becomes the backing of `DeckAudioSource`.

NAudio 2.2.1 is already referenced and provides `WasapiLoopbackCapture` and
`MMDeviceEnumerator` — no new audio dependency is required for Phase 1.

## Proposed interfaces

```csharp
public interface IAudioSource : IDisposable
{
    string Name { get; }
    bool IsRunning { get; }
    WaveFormat Format { get; }      // NAudio.Wave.WaveFormat
    void Start();
    void Stop();

    // Raised on the capture/playback thread with newly available samples.
    event EventHandler<AudioSamplesAvailable>? SamplesAvailable;
}

public sealed record AudioSamplesAvailable(
    ReadOnlyMemory<float> Interleaved,  // source-native channels/sample rate
    int Channels,
    int SampleRate);
```

`IAudioSource` only *produces* raw samples. Conversion to analysis-ready frames is
the frame pipeline's job (doc 02) — strict layer separation (global standard #4).

## Initial sources

### `DeckAudioSource`

Wraps `PlaylistAudioPlayer`. Emits the samples currently being played by the
internal deck. This is the default source and preserves today's behavior: with
Live Mode off, the deck is the only source.

### `SystemLoopbackAudioSource`

Captures the Windows system output mix via NAudio `WasapiLoopbackCapture`.

```csharp
public sealed class SystemLoopbackAudioSource : IAudioSource
{
    // ctor takes the selected MMDevice (render endpoint) to capture.
    // Start():  create WasapiLoopbackCapture, subscribe DataAvailable,
    //           convert byte[] -> float[] per WaveFormat, raise SamplesAvailable.
    // Stop():   StopRecording + dispose capture.
}
```

Responsibilities:

- Enumerate render endpoints (`MMDeviceEnumerator.EnumerateAudioEndPoints(Render,
  Active)`) and expose them for device selection in the UI.
- Convert captured bytes to `float` PCM according to the capture `WaveFormat`
  (loopback is typically 32-bit IEEE float, but handle 16-bit PCM defensively).
- Surface "device changed / default device moved" via NAudio notifications so the
  UI can prompt re-selection. Never crash the render loop on device loss
  (global standard #16, #26).

### `SoundCardInputAudioSource` (WASAPI or ASIO backend)

Captures audio from an external sound card / audio interface input (line-in), as
opposed to the system output mix. This is the path for capturing a hardware DJ
mixer's output, or the master of an external DJ app, into Liveolator's visuals.

Two backends behind one source, selectable per device (see "Audio I/O backends"
below):

- **WASAPI capture** (`WasapiCapture` on a capture `MMDevice`) — works with any
  Windows sound card, shared or exclusive mode.
- **ASIO capture** (NAudio `AsioOut`) — low-latency path for pro/DJ interfaces that
  ship an ASIO driver (the **Behringer CMD STUDIO 2A** built-in 4-channel interface
  is such a device — see doc 07). Required when latency matters for tight visuals.

## Audio I/O backends: WASAPI vs ASIO

The audio layer must support real sound cards, including low-latency **ASIO**. The
backend is an implementation detail behind `IAudioSource` (input) and the deck output
path (doc 11) — the rest of the app never sees it.

```csharp
public enum AudioBackend { WasapiShared, WasapiExclusive, Asio }

public interface IAudioDeviceCatalog
{
    IReadOnlyList<AudioDeviceInfo> EnumerateRenderEndpoints();   // WASAPI render
    IReadOnlyList<AudioDeviceInfo> EnumerateCaptureEndpoints();  // WASAPI capture
    IReadOnlyList<AudioDeviceInfo> EnumerateAsioDrivers();       // AsioOut.GetDriverNames()
}

public sealed record AudioDeviceInfo(
    string Id, string Name, AudioBackend Backend, int InputChannels, int OutputChannels);
```

ASIO specifics (NAudio `AsioOut`):

- Drivers enumerated via `AsioOut.GetDriverNames()`; the user selects one in the
  Mappings/Audio UI (doc 12).
- Input capture subscribes to `AsioOut.AudioAvailable` and reads interleaved float
  samples for the selected input channels.
- ASIO is **exclusive** — one application owns the driver at a time. If an external
  DJ app holds the CMD STUDIO 2A's ASIO driver, Zalmanolator cannot also open it;
  the UI must detect this and fall back to a loopback/system-mix capture or another
  input. This trade-off is documented for the performer.
- ASIO buffer size is driver-controlled; surface the reported latency in the UI as a
  diagnostic (doc 14 metric).

The same backend abstraction serves **output** for the deck/headphone-cue path in
doc 11. Because the user confirmed **Liveolator is the DJ player**, multi-channel
**output** (master on ch 1/2, headphone cue on ch 3/4 of the CMD STUDIO 2A) over the
low-latency backend is a **confirmed requirement**, not conditional.

> When Liveolator plays its **own** deck through a low-latency output device, the beat
> engine already has the deck samples directly (via `DeckAudioSource`) — no capture
> is needed. Capture (loopback/line-in) is for audio that originates outside the app.

## Future sources (designed for, not built in Phase 1)

- `ProcessLoopbackAudioSource` — per-process capture via the newer Windows
  process-loopback API (Windows 10 2004+). Behind capability detection.
- `NetworkClockAudioSource` — not a PCM source; an external tempo/clock source for
  Ableton Link or DJ-link sync. Implements a separate `IExternalClock` rather than
  `IAudioSource`. Noted here for completeness; see doc 03.

## Ring buffer

A single-producer/multi-consumer ring buffer decouples the capture thread from the
render loop and beat analysis (per the threading model in doc 00).

```csharp
public sealed class AudioRingBuffer   // float samples, mono or interleaved
{
    public AudioRingBuffer(int capacitySamples);
    public void Write(ReadOnlySpan<float> samples);   // producer (capture thread)
    public int Read(Span<float> destination);          // consumer; returns count
    public int Available { get; }
}
```

- Capacity sized for the worst-case analysis window (beat engine wants 8–12 s of
  history — see doc 03) plus headroom.
- Overwrites oldest samples when full; capture must never block (dropping stale
  audio is correct for a live visualizer).

## Low-latency targets & the real-time audio thread

> Library-independent; from `docs/research/dj-gaps-keydetect-latency-automix-avsync.md`.
> Drives the doc 00 "low-latency audio" requirement and the deck output path (doc 11).

**The callback model.** The OS audio backend requests a buffer of samples on a **realtime,
performance-sensitive callback thread**, hundreds of times per second. Each buffer must be
fully computed within **one buffer period** or the audio glitches (xrun / under-run). For a
256-sample buffer at 44.1 kHz that deadline is **< 5.8 ms**, every time, no exceptions.

**Targets (both platforms):**

- Aim for **< 10 ms** output latency; default buffer **~256–512 samples**, exposed as a
  user setting (Mixxx ships 23–64 ms as "safe", < 10 ms for tight/timecode use).
- Latency floor cannot go below one callback buffer period.
- Total latency = the **sum of stacked buffer layers** (driver + backend + app), so keep
  the chain short, not just the buffer small.
- Backends: **Windows** ASIO / WDM-KS (good) › WASAPI (acceptable); **macOS** CoreAudio
  (single option, behaves like a clean double-buffer). Reach them through the seam; app code
  never sees the backend.

**Hard rules for the audio callback thread (non-negotiable):**

- **No memory allocation/free** (`new`/`delete`/`malloc`) on the audio thread.
- **No locks/mutexes**, no blocking on semaphores, disk, or network.
- **No calls into OS/3rd-party code that may block internally**, and no unbounded-time ops.
- Cross-thread communication (UI/MIDI/`PerformanceAction` → audio) is **lock-free**:
  pre-allocated ring buffers / atomics (see "Ring buffer" above). The dispatcher (doc 04)
  hands actions to the audio thread via a lock-free queue; it never mutates audio state
  under a lock.
- Pre-allocate all buffers; size worst-case up front.

These rules apply to the deck mixer/DSP (doc 11) and any per-sample work, regardless of the
chosen audio library.

## Source selection

A small `AudioSourceManager` owns the active `IAudioSource`, exposes the available
sources/devices, and handles switching at runtime (Deck ↔ System loopback ↔ Input)
without tearing down the frame pipeline. Switching is itself a `PerformanceAction`
(`Beat.SetSource` / a `Source.Select` action) — see doc 04.

## Error handling & logging

- Wrap `Start()/Stop()` and the WASAPI/ASIO callback in try/catch with contextual
  logging (device name, backend, format) — never an empty catch (global standard #16).
- On capture failure, stop cleanly, raise a surfaced error event, and fall back to
  the deck source so visuals keep running.
- ASIO driver already in use (exclusive) → surface a clear, actionable message and
  offer WASAPI loopback as the fallback; do not throw into the render loop.
- Never log raw audio buffers or device identifiers that could be sensitive.

## Phase

- Phase 1 (Live Audio Capture MVP): `DeckAudioSource` + `SystemLoopbackAudioSource` +
  WASAPI device enumeration + ring buffer + signal meter.
- Phase 1b (sound card / ASIO): `SoundCardInputAudioSource` with the WASAPI/ASIO
  backend abstraction + `IAudioDeviceCatalog` (incl. `AsioOut.GetDriverNames()`).
  Lands alongside or just after the loopback MVP so real interfaces (CMD STUDIO 2A)
  are supported early.

Success criteria (from the plan): Spotify/YouTube/system audio drives the visuals with
no file loaded; deck playback still works; no UI freeze during capture; a low-latency
interface can be selected as the capture/output device.

## Risks

- Loopback captures the **selected output mix**, not a single app, unless the
  process-loopback API is used. Document this clearly in the UI.
- ASIO is exclusive: capturing from the same interface an external DJ app is using is
  impossible; the WASAPI-loopback fallback must be obvious to the performer.
- ASIO driver quality varies; report driver-reported latency and validate the channel
  layout before relying on it.
- Some audio drivers misbehave with loopback (exclusive-mode streams, odd formats).
  Validate format and degrade gracefully.
- Sample-rate mismatch between source and the FFT expectation is handled in the
  frame pipeline (doc 02), not here.
