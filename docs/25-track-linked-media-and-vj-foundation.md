# 25 - Track-Linked Media Library and VJ Foundation

> **Status:** implementation plan, 2026-06-08.
>
> **Product decision:** the foundation of the VJ system is media linked to music tracks.
> When a track plays, its assigned images and video clips play automatically on the VJ
> output. Scenes, GLSL effects, visual objects, manual performance controls, and autopilot
> are added above this foundation; they are not prerequisites for it.

## 1. Outcome

The first complete vertical slice must let a user:

1. Scan folders containing images and video clips into the visual library.
2. Select a music track and assign one or more visual assets to it.
3. Order the assigned assets and configure their basic playback behavior.
4. Save the assignment as authored user data.
5. Load and play the track on either deck.
6. See its assigned media start, pause, seek, resume, loop, and end with the deck.
7. Mix two tracks while each deck's base visual layer follows that deck's audible gain.
8. Continue the show when an asset is missing or cannot be decoded.

The basic path must work without scenes, effects, Push, camera input, or autopilot.

## 2. Existing Building Blocks

Do not rebuild these:

- `VisualMediaLibrary` scans images and video files.
- `VisualAsset` stores path, kind, dimensions, duration, and probe status.
- `JsonCatalogStore` persists the regenerable visual catalog and scan folders.
- `VisualLibraryViewModel` provides the current VJ asset browser.
- `VisualScene`, `VisualLayer`, `VisualSourceRef`, and `IVisualPerformanceEngine` define
  the platform-neutral visual model and engine seam.
- `GlVisualPerformanceEngine` renders multi-layer still-image compositions.
- The shared `IBeatClock` / `IBeatTimeline` provides audio/visual beat synchronization.
- Deck, mixer, and visual commands already pass through the shared
  `PerformanceActionDispatcher`.

Important prerequisites identified by the system review:

- Make the GL render loop apply live scene/layer/source changes.
- Add framebuffer-size viewport handling for Windows/macOS parity.
- Implement video as a renderable visual source.

## 3. Product Model

### 3.1 Separate catalog data from authored links

The catalogs are caches and may be deleted or rebuilt. Track-to-media assignments are
authored user work and must live in a separate, versioned store.

```text
catalog.music.json                 regenerable music facts
catalog.visual.json                regenerable visual facts
live/track-visuals/<track-key>.json authored track-to-media program
```

Never embed the assignment only inside `MusicTrack` or `VisualAsset`.

### 3.2 Track visual program

Add pure Core records under `Liveolator.Core/Visuals/TrackPrograms/`:

```csharp
public sealed record TrackVisualProgram(
    string Id,
    TrackReference Track,
    IReadOnlyList<TrackVisualCue> Cues,
    TrackVisualFallback Fallback,
    int SchemaVersion);

public sealed record TrackReference(
    string Path,
    long SizeBytes,
    DateTime LastModifiedUtc,
    string? Artist,
    string? Title,
    TimeSpan? Duration);

public sealed record TrackVisualCue(
    string Id,
    VisualAssetReference Asset,
    TimeSpan StartAt,
    TimeSpan? EndAt,
    TimeSpan? SourceIn,
    TimeSpan? SourceOut,
    VisualFitMode Fit,
    VisualPlaybackMode Playback,
    TransitionStyle Transition,
    double Opacity);

public sealed record VisualAssetReference(
    VisualMediaKind Kind,
    string Path,
    long SizeBytes,
    DateTime LastModifiedUtc);
```

The first schema deliberately uses paths plus cheap fingerprints, matching the existing
catalog. A later migration may add content IDs or acoustic fingerprints for automatic
relinking without changing the runtime contract.

### 3.3 Cue semantics

- `StartAt` is a position on the original track timeline.
- `EndAt = null` means "until the next cue or the end of the track."
- An image remains visible for its cue interval.
- A video uses `SourceIn`/`SourceOut`; omitted values mean the full clip.
- `Playback.Once` holds the last frame until the cue ends.
- `Playback.Loop` loops the selected source range until the cue ends.
- `Playback.Stretch` is deferred; it requires explicit time-stretch policy.
- Overlapping cues are allowed in a later multi-layer editor, but phase 1 validates a
  single ordered base-media cue at any track position.
- Empty programs use the configured fallback instead of producing an error.

### 3.4 Default program creation

The editor should make the common case fast:

- Assign one image: display it for the whole track.
- Assign one video: loop it for the whole track.
- Assign multiple assets: distribute them evenly across the known track duration.
- If duration is unknown: play assets sequentially using a configurable default image
  duration and each video's natural duration, then loop the list.

The user can later edit exact cue points.

## 4. Runtime Architecture

### 4.1 One base layer per deck

Reserve two compositor layers:

```text
Deck A Base Media
Deck B Base Media
```

Each layer is driven by the visual program of the track loaded on that deck. Its opacity is:

```text
program cue opacity x audible deck gain
```

The audible gain must include channel gain and crossfader gain. This gives a natural
visual blend during an audio mix:

- deck A audible only -> A media visible;
- center mix -> both media blended;
- deck B audible only -> B media visible.

EQ does not change visual opacity in the first increment. A later energy-reactive mode may
use post-EQ meters.

### 4.2 Track visual coordinator

Add a pure orchestration service in Core:

```text
Deck/mixer read models
        |
        v
TrackVisualCoordinator
        |
        v
PerformanceActionDispatcher
        |
        v
VisualActionHandler -> IVisualPerformanceEngine
```

Responsibilities:

- Resolve the loaded track's `TrackVisualProgram`.
- Preload the first required asset when a track is loaded.
- Activate the program when playback begins.
- Derive the active cue from the deck's original-media position.
- Pause visual time when the deck pauses.
- Re-resolve immediately after seek, cue, hot-cue, or loop wrap.
- Follow pitch/tempo changes without losing track-position synchronization.
- Clear or fade the deck layer when the track stops or ends.
- Update each deck visual layer from its audible mixer gain.
- Emit visual intent through the dispatcher; never call the GL engine directly.

The coordinator may read a platform-neutral playback snapshot seam, but engine mutations
remain action-driven.

### 4.3 Read-only playback state

Add a Core seam that exposes immutable snapshots:

```csharp
public interface IDeckPlaybackStateProvider
{
    DeckPlaybackSnapshot GetSnapshot(int slot);
}

public sealed record DeckPlaybackSnapshot(
    int Slot,
    string? TrackPath,
    bool IsPlaying,
    double OriginalPositionSeconds,
    double EffectiveRate,
    bool IsLooping);
```

`TwoDeckBassEngine` can implement or adapt to this seam. The coordinator polls snapshots
from the existing performance clock loop at a bounded cadence. It must not run from the
Avalonia UI timer.

### 4.4 New visual actions

Keep action payloads serializable and small. Add actions only for missing intent:

- `VisualLoadTrackProgram`: `Slot` = deck, `Argument` = program ID.
- `VisualSetTrackPosition`: `Slot` = deck, `Value` = original track seconds.
- `VisualSetDeckLayerGain`: `Slot` = deck, `Value` = 0..1.
- `VisualStopTrackProgram`: `Slot` = deck.

The visual handler resolves program IDs through an injected
`ITrackVisualProgramRepository`. Do not serialize a full program into an action string.

High-frequency position updates should be coalesced. If the current cue has not changed,
the video source receives a clock/snapshot update without rebuilding the whole scene.

## 5. Video Source Pipeline

### 5.1 Boundary

All FFmpeg/native decode stays in `Liveolator.Visuals`. Core owns only source references,
playback intent, and immutable state.

### 5.2 Components

Add:

- `IVideoFrameDecoder`: open, seek, decode-next, close.
- `FfmpegVideoFrameDecoder`: FFmpeg binding/process implementation.
- `VideoVisualSource`: owns decoder state and the latest uploadable frame.
- `VisualSourceFactory`: creates image/video/camera sources from `VisualSourceRef`.
- `TextureUploadQueue`: transfers decoded frames to GL only while the context is current.
- `VisualSourceCache`: bounded cache for prepared images and opened upcoming clips.

Decode runs off the GL thread. Texture creation/upload and deletion run on the GL thread.
The render loop never waits for disk or FFmpeg.

### 5.3 Timing

Presentation is keyed to original track media time:

```text
cueLocalTime = deckOriginalPosition - cue.StartAt
sourceTime = SourceIn + cueLocalTime modulo selectedSourceDuration
```

This keeps seeking, loops, hot cues, and pitch changes deterministic. The decoder may drop
late frames; it must not delay audio or the render loop to display every frame.

### 5.4 Decode policy

Initial target:

- H.264/H.265/VP9 and common image formats through FFmpeg/Skia capabilities.
- Decode to RGBA or a supported YUV upload path.
- At most one active and one prepared video per deck base layer.
- Bounded frame queues, initially 2-4 frames.
- Configurable maximum decode resolution; preserve aspect ratio.
- Hold the last good frame during a short decode stall.
- Replace an irrecoverable source with transparent/fallback output and log once.

Hardware decode is an optimization after the software path is correct on Windows and
macOS.

## 6. Compositor Changes

Implement these before track-linked playback is considered functional:

1. Add a render-state dirty flag or command queue.
2. Apply source/layer/scene changes at frame start on the GL thread.
3. Handle framebuffer resize and Retina framebuffer dimensions.
4. Make image and video sources implement one frame/texture contract.
5. Keep deck base layers separate from optional scene/effect/object layers.
6. Preserve current blackout and beat-reactive uniforms.
7. Add transition support only after hard source switching is reliable.

Recommended layer groups:

```text
0  Deck A base media
1  Deck B base media
2+ Scene overlays / objects / camera / text
N  Master overlays such as strobe and blackout
```

This is a logical grouping; the renderer may use a different internal index layout.

## 7. Persistence

Add Core seams:

```csharp
public interface ITrackVisualProgramStore
{
    Task<TrackVisualProgram?> LoadAsync(string trackPath, CancellationToken ct = default);
    Task SaveAsync(TrackVisualProgram program, CancellationToken ct = default);
    Task DeleteAsync(string trackPath, CancellationToken ct = default);
    Task<IReadOnlyList<TrackVisualProgramSummary>> ListAsync(CancellationToken ct = default);
}
```

Implement in `Liveolator.Media` using the existing snapshot conventions:

- versioned JSON;
- atomic temp-then-move writes;
- serialized concurrent saves;
- tolerant corrupt/unknown-version loads;
- warnings with file context;
- authored files never silently overwritten by catalog scans.

Store files under:

```text
<app-data>/Liveolator/live/track-visuals/<safe-track-key>.json
```

The safe key can initially be a SHA-256 hash of the normalized track path. The full path
and fingerprint remain inside the file for validation and relinking.

## 8. User Experience

### 8.1 Visual library tab

Evolve the current asset browser into three areas:

- **Library:** folders, scan, search, image/video/status filters.
- **Preview:** selected image or muted video preview, metadata, missing/failed state.
- **Assignment:** tracks currently using the asset and an "Assign to track" action.

Add thumbnails asynchronously with bounded caching. A failed thumbnail must not affect
catalog status or runtime playback.

### 8.2 Music library track inspector

For a selected music track, add a `Visuals` section:

- assigned asset count;
- visual program status: none, ready, missing assets, invalid;
- `Edit Visuals` command;
- quick assignment by drag/drop from the visual library;
- `Preview with Track` command.

### 8.3 Track visual editor

First editor:

- track waveform/timeline across the top;
- ordered cue lane below it;
- asset browser/drag source;
- cue start/end handles;
- image duration;
- video in/out, once/loop;
- fit mode: contain, cover, stretch;
- transition selector, initially Cut only enabled;
- preview transport linked to the track;
- missing-asset warning and Relink action;
- Save, Revert, Remove Program.

The editor writes one immutable program snapshot on save. Intermediate edits stay in the
view-model and do not alter live playback until applied.

### 8.4 Live mode

Show small status indicators:

- deck A/B visual program loaded;
- current asset title;
- missing/decode warning;
- base-layer activity;
- manual override active.

Manual scene launching overlays or temporarily overrides the base layers according to an
explicit policy. Default policy: track media continues underneath scene overlays.

## 9. Missing Files and Relinking

Resolution order:

1. Exact stored path.
2. Current catalog entry with the same normalized path.
3. Unique catalog candidate matching kind, size, and filename.
4. User-selected relink.

Never silently choose between multiple candidates. Save the corrected reference only after
an unambiguous automatic match or explicit user confirmation.

Runtime behavior for an unresolved cue:

- skip to the next valid cue when possible;
- otherwise render the track's configured fallback;
- log one warning per asset/program activation, not once per frame.

## 10. Fallback Policy

Per program:

- `Transparent`: reveal lower scene layers.
- `SolidColor`: configurable color.
- `AlbumArt`: deferred until artwork extraction is available.
- `GlobalDefaultProgram`: play an app/user default visual loop.

Recommended default is `GlobalDefaultProgram`, with `Transparent` as its final fallback.

## 11. Performance Budgets

Initial measurable targets:

- 60 fps output at 1920x1080 on the supported baseline machine.
- No disk, JSON, image decode, or video decode on the GL thread.
- No allocations in the per-frame Core coordinator path after warm-up.
- Source switch visible within one rendered frame after its scheduled activation.
- A/V visual reaction target below 50 ms after configured output-latency compensation.
- Bounded memory: two active videos, two prepared videos, bounded thumbnail cache.
- Frame dropping is allowed; audio interruption is never allowed.

Expose diagnostics for render FPS, decode FPS, dropped frames, active source, queue depth,
and visual clock offset.

## 12. Testing Strategy

### Core unit tests

- Program validation and cue ordering.
- Active-cue resolution at boundaries.
- Image hold, video once, and video loop time math.
- Pause/resume, seek, hot-cue, and loop-wrap re-resolution.
- Deck A/B independence.
- Audible-gain to opacity mapping.
- Missing-program and missing-asset fallback selection.
- Coordinator emits actions only when state meaningfully changes.
- Serialization-safe action payloads.

### Media tests

- Program JSON round trip.
- Atomic save and concurrent-save serialization.
- Corrupt and newer-version tolerance.
- Safe path-key generation.
- Relink candidate selection and ambiguity rejection.

### Visuals tests

- Fake decoder frame scheduling and frame dropping.
- Decode cancellation/disposal.
- Texture upload command ordering.
- Source cache eviction.
- Live source swap reaches the next composed frame.
- Resize uses framebuffer dimensions.
- Decode failure leaves the render loop alive.

### App tests

- Assign/remove/reorder assets.
- Editor validation and dirty/revert/save states.
- Restored assignment appears on both library surfaces.
- Missing asset exposes Relink.
- Live deck indicators follow coordinator state.

### Integration/manual matrix

- Windows and macOS.
- Image-only program.
- One looping video.
- Mixed images and videos.
- Two decks crossfading.
- Pause, seek, loop, hot cue, pitch change.
- Missing file while the app is closed.
- Corrupt video during playback.
- Retina/high-DPI resize and fullscreen.

## 13. Delivery Plan

### Milestone 0 - Stabilize current compositor

- Fix live GL state application.
- Fix framebuffer viewport/resize.
- Confirm scene/source changes render after the window opens.

**Exit:** a runtime image source swap is visible without restarting the visual window.

### Milestone 1 - Authored track-media model

- Add Core program records, validation, cue resolver, and store seam.
- Add JSON store and tests.
- Add assignment status to music and visual library view-models.

**Exit:** assignments survive restart and missing assets are reported.

### Milestone 2 - Image-only automatic playback

- Add deck playback snapshot seam.
- Add `TrackVisualCoordinator`.
- Reserve deck A/B base layers.
- Synchronize image cues to load/play/pause/seek/stop.
- Map audible deck gains to base-layer opacity.

**Exit:** two decks can mix, each with an ordered image program.

### Milestone 3 - Video source

- Add decoder seam and FFmpeg implementation.
- Add async decode, bounded queue, GL texture upload, seek, loop, and disposal.
- Integrate video cues into the same coordinator/program flow.

**Exit:** a linked video remains synchronized through pause, seek, loop, and pitch change.

### Milestone 4 - Track visual editor

- Build timeline editor, drag/drop assignment, preview, in/out, loop, fit, and relink.
- Add thumbnail and muted-preview caches.

**Exit:** the full assignment workflow is usable without editing JSON.

### Milestone 5 - Transitions and beat-aware cueing

- Add cut/fade/dissolve transitions.
- Add optional beat/bar snapping for cue boundaries.
- Pre-roll upcoming assets before a scheduled boundary.

**Exit:** cue changes can be quantized to the shared beat timeline without stalling render.

### Milestone 6 - Effects and visual objects

- Execute per-layer GLSL `EffectRef` chains.
- Bind macros to effect/source parameters.
- Add overlay object layers such as shapes, particles, text, and camera.
- Keep track media as the stable base layer.

**Exit:** effects and objects can be added, removed, and controlled without changing the
track-media assignment model.

## 14. Scope Boundaries

Not required for the foundational release:

- camera/capture sources;
- hardware video decoding;
- optical-flow transitions;
- multiple overlapping authored cue lanes;
- generative visual objects;
- automatic media recommendation;
- cloud sync or shared asset packages;
- exporting rendered video.

These may be added later without replacing the program/store/coordinator design.

## 15. Definition of Done

The foundation is done when:

- a user can assign images/videos to any catalogued track from the UI;
- assignments persist independently from regenerable catalogs;
- playback automatically activates the correct program on either deck;
- visuals follow play, pause, seek, loop, hot cue, pitch, stop, and end-of-track;
- two deck programs blend according to their audible mixer gains;
- missing/corrupt media never crashes audio, UI, or the render loop;
- Windows and macOS pass the manual matrix;
- all pure behavior is covered without requiring GL, FFmpeg, audio hardware, or MIDI in CI.
