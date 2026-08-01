# Integrations and Side Effects

- Last updated: 2026-08-01
- Scope analyzed: Native bindings, external processes/services, filesystem stores, and agent-facing interfaces.
- Confidence note: High for adapters present in code; runtime availability is configuration-dependent.

| Integration | Side effect | Failure posture |
| --- | --- | --- |
| BASS/ManagedBass | Realtime decode, decks, mix, output, capture | App can degrade without an initialized native engine |
| RtMidi | Opens MIDI devices and sends LED/feedback messages | Missing/open failures are logged and MIDI remains unavailable |
| OpenGL/Silk.NET | Opens and renders the visual stage | Requires compatible graphics/runtime context |
| FFmpeg/ffprobe | Decode/probe/thumbnail and offline audio work | Per-file/process failures are isolated where implemented |
| Python analysis runtime | Song structure and stem separation | Optional install/runtime; advanced analysis may be unavailable |
| AcoustID/GetSongBPM | Fingerprint/metadata lookup over HTTP | Requires credentials/tools; failures should not destroy local analysis |
| JSON/SQLite stores | Catalog, settings, queues, cues, projects, mappings, extensions | Stores generally use tolerant load and atomic replacement patterns |
| Filesystem imports | Reads Rekordbox, Serato, Traktor, Mixxx, Engine, and VirtualDJ data | Path resolution and merge policy decide usable records |
| MCP stdio server | External agents read/analyze/author through tools | Exposes DTOs over selected Core services, not arbitrary UI control |
| System browser/update manifest | Checks releases and opens download URL | Invalid/unreachable manifest yields no update prompt |

Recording and offline render write user-selected media files. Extension installation writes package content and registry/trust state. Library repair/relocation can rewrite references and, where explicitly requested, remove files; these are material side effects and require orchestration-level confirmation.

## Code References

- `src/Liveolator.Audio/` — native audio adapters
- `src/Liveolator.Midi/RtMidiDeviceProvider.cs`
- `src/Liveolator.Visuals/Gl/GlVisualPerformanceEngine.cs`
- `src/Liveolator.Online/AcoustIdClient.cs`
- `src/Liveolator.Media/SqliteCatalogStore.cs`
- `src/Liveolator.Mcp/Tools/LibraryTools.cs`
