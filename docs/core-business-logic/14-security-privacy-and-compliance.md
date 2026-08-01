# Security, Privacy, and Compliance

- Last updated: 2026-08-01
- Scope analyzed: Trust controls, local data, external transmission, code/process loading, and incident-readiness signals.
- Confidence note: Medium; this is a code posture assessment, not a legal compliance certification.

## Security posture

Extension validation is the clearest security control: package structure, paths, hashes/signatures, dependencies, and trusted publishers are represented in code, with developer mode providing an intentionally weaker path. Native plugins, GLSL/shader probing, FFmpeg/Python helpers, and external executables expand the attack surface and should remain isolated, time-bounded, and least-privileged.

The desktop and MCP processes inherit the operating-system user's authority. No application-level role model protects media files or stores. If MCP remains stdio-local this is a manageable trust assumption; any network exposure would require authentication, authorization, request limits, and filesystem scope enforcement.

## Privacy and data handling

The system may persist absolute media paths, track metadata and analysis, cue/play history, settings, mappings, projects, visual programs, fingerprints/caches, recordings, renders, extension trust, and logs. Online enrichment can transmit fingerprints and track-identifying metadata to configured providers. The repository shows functional storage and provider adapters but no complete retention, deletion, export, consent, or incident-response policy.

## Compliance gaps to resolve

- Needs validation: privacy notice and consent for third-party metadata lookups.
- Needs validation: retention/deletion policy for user-generated recordings and catalog artifacts.
- Needs validation: secrets storage and log redaction for API keys, paths, and provider responses.
- Needs validation: software-license and redistribution obligations for BASS, FFmpeg, codecs, native plugins, and bundled analysis tools.

## Code References

- `src/Liveolator.Media/Extensions/ExtensionPackageValidator.cs`
- `src/Liveolator.Media/Extensions/JsonTrustedPublisherStore.cs`
- `src/Liveolator.Online/AcoustIdClient.cs`
- `src/Liveolator.Online/GetSongBpmClient.cs`
- `src/Liveolator.Core/Settings/OnlineSettings.cs`
- `THIRD-PARTY-NOTICES.txt`
