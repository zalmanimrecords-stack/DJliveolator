# Open Questions and Assumptions

- Last updated: 2026-08-01
- Scope analyzed: Contradictions, incomplete flows, runtime-sensitive behavior, and evidence gaps.
- Confidence note: Each item is intentionally uncertain and labeled.

## Needs validation

- Which features are considered production-supported on macOS today, including BASS/CoreAudio routing, notarization, MIDI device behavior, and camera capture.
- Whether arbitrary visual effect chains are fully active in the current render loop; historical status documentation describes earlier limitations and may be stale.
- Whether native VST3 scanning/bridging executables are shipped in current distributions.
- Whether four-deck studio playback has been manually verified alongside uninterrupted two-deck live use on supported hardware.
- What transaction/concurrency guarantees are required when the App and MCP process access the same catalog or JSON stores concurrently.
- Whether library repair operations that remove or rewrite user files always require an explicit preview and confirmation in every entry point.
- What privacy/retention policy applies to track paths, fingerprints, online lookup payloads, logs, recordings, and generated analysis artifacts.

## Unclear from code

- A single authoritative definition of “release-ready” across Windows installer, macOS packaging, native dependencies, code signing, and update publishing.
- Whether all authored preset/add-on formats have a documented backward-compatibility policy.
- Whether MCP is guaranteed to remain local stdio-only.

## Assumptions used in this documentation

- The implementation under `src/` and executable tests outrank roadmap prose when they disagree.
- “Actor” means a code-visible user or client interaction, not a formal authenticated role.
- Pure-Core behavior is considered implemented even when a native adapter or UI exposure may be unavailable.

## Code References

- `docs/18-implementation-status.md`
- `docs/31-system-review-2026-06-27.md`
- `src/Liveolator.App/Composition/ServiceConfig.cs` — `ServiceConfig`
- `src/Liveolator.Mcp/Program.cs`
- `scripts/build-installer.ps1`
