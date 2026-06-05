# Liveolator.Audio — module rules

**Purpose:** audio binding — (1) offline decode for analysis (WAV pure-managed, other
formats via the FFmpeg CLI, composed behind one decoder) and (2) realtime playback via
BASS, implementing the Core `IAudioSource` seam.

**Design source of truth:** [`docs/16`](../../docs/16-track-analysis-library.md) ·
[`docs/17`](../../docs/17-mcp-agent-interface.md). (`docs/01`/`docs/02` still describe
the old realtime stack — pending revision.)

## Iron rules

1. **Implements the Core seams `IAudioDecoder` + `IAudioSource`**
   (`Liveolator.Core.Analysis` / `Liveolator.Core.Audio`). Core depends on the
   interfaces, never on this assembly.
2. **Realtime playback uses BASS/ManagedBass** (decided 2026-06-05). All BASS calls go
   through the internal `IBassPlayback` seam so `DeckAudioSource` unit-tests with a fake;
   the native bass library is not present in CI. The public entry point is
   `BassAudioEngine`. Offline decode stays separate (WAV managed + FFmpeg CLI).
3. **FFmpeg is invoked as a CLI process, not native bindings.** Process/exit failures
   must be handled and logged, never swallowed (global standard #16).

**Tests:** `tests/Liveolator.Audio.Tests` (unit) and `tests/Liveolator.Integration.Tests`
(real FFmpeg).
