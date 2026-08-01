# High-Priority Recommendations

- Last updated: 2026-08-01
- Scope analyzed: Engineering, product, security, privacy, and operational priorities implied by implemented behavior.
- Confidence note: Medium; recommendations are judgments, while cited risks are code-grounded.

## Priority actions

1. Establish executable release gates for Windows and macOS: native dependency presence, audio routing, cue output, MIDI feedback, visual rendering, recording, update flow, signing, and upgrade/uninstall preservation.
2. Define and test a single-writer or transactional policy for catalog and live JSON state when App and MCP run concurrently. Prefer SQLite transactions or explicit cross-process locking for shared mutable data.
3. Put destructive library repair behind a mandatory preview, exact target list, explicit confirmation, and recoverable operation where possible. Test reference rewrites across playlists, cues, projects, and visual programs.
4. Threat-model extension archives, shader/process execution, Python installation, VST3 bridging, and MCP file access. Keep developer mode visibly separate from trusted production behavior.
5. Add end-to-end performance soak tests for deck end/queue advance, clock switching, sync correction, four-deck Studio playback, device reconnect, and degraded native dependencies.
6. Centralize product-significant App-only policies when more than one surface needs them. Played history, startup restoration, and load/queue decisions should have reusable, headless tests.
7. Version all authored/persisted formats and publish migration/compatibility guarantees before third-party preset and extension ecosystems grow.
8. Define retention and redaction rules for absolute media paths, logs, fingerprints, online metadata requests, recordings, renders, caches, and crash diagnostics.

## Code References

- `src/Liveolator.Media/SqliteCatalogStore.cs`
- `src/Liveolator.Core/Library/LibraryDoctor.cs`
- `src/Liveolator.Core/Library/LibraryReferenceRewriter.cs`
- `src/Liveolator.Media/Extensions/ExtensionPackageValidator.cs`
- `src/Liveolator.Core/Audio/Sync/PhaseLockController.cs`
- `src/Liveolator.App/Composition/ServiceConfig.cs` — `ServiceConfig`
