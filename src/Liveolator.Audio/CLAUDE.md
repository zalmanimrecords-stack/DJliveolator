# Liveolator.Audio — module rules

**Purpose:** offline audio decode for analysis — WAV via a pure-managed decoder, all
other formats via the FFmpeg CLI, composed behind one decoder.

**Design source of truth:** [`docs/16`](../../docs/16-track-analysis-library.md) ·
[`docs/17`](../../docs/17-mcp-agent-interface.md). (`docs/01`/`docs/02` still describe
the old realtime stack — pending revision.)

## Iron rules

1. **Implements the Core seam `IAudioDecoder`** (`Liveolator.Core.Analysis`). Core
   depends on the interface, never on this assembly.
2. **Offline / analysis decode ONLY.** The realtime playback library (BASS vs
   PortAudio/miniaudio) is still an **open decision** — do not add a playback path here
   until it is made (project `CLAUDE.md` → open decisions).
3. **FFmpeg is invoked as a CLI process, not native bindings.** Process/exit failures
   must be handled and logged, never swallowed (global standard #16).

**Tests:** `tests/Liveolator.Audio.Tests` (unit) and `tests/Liveolator.Integration.Tests`
(real FFmpeg).
