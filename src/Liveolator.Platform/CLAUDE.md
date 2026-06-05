# Liveolator.Platform — module rules

**Purpose:** OS-backed concrete implementations of Core seams (today the filesystem
enumerator; later, platform device/MIDI bindings).

**Design source of truth:** [`docs/00`](../../docs/00-architecture-overview.md).

## Iron rules

1. **Implementations of Core interfaces only** — no domain logic. Core stays unaware
   of this assembly; it is wired in at the composition root.
2. **Cross-platform mandatory** — every implementation must work on Windows **and**
   macOS (project `CLAUDE.md`).
3. **Tolerate per-item OS failures** — skip the bad file/device and continue; never
   abort the whole operation (see [`FileSystemEnumerator.cs`](FileSystemEnumerator.cs)).
