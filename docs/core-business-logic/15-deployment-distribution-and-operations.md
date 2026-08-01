# Deployment, Distribution, and Operations

- Last updated: 2026-08-01
- Scope analyzed: Build scripts, installer, update path, runtime dependencies, persistence, and operational degradation.
- Confidence note: High for repository automation; Medium for externally signed/published artifacts and hardware support.

## Distribution model

The repository contains a self-contained Windows publishing and Inno Setup path. The build retrieves/verifies required BASS native files, publishes the Avalonia app, checks critical payload files, and builds a per-user installer. User data under the application-data root is intended to survive upgrades and uninstall. A macOS distribution/notarization workflow is not evident at the same maturity.

## Runtime dependencies

Operational capability depends on BASS native libraries, RtMidi, OpenGL drivers, FFmpeg/ffprobe, optional Python analysis environments, optional fingerprint utilities/API credentials, and possibly native VST3 bridge/scanner components. Startup composition is designed to degrade when optional devices or services are absent, but each release should verify which features remain visible and how failures are communicated.

## Updates and supportability

Startup update checking fetches a static manifest, compares versions conservatively, and lets the user download, skip, or defer. Publishing integrity, HTTPS hosting, code signing, rollback, and compatibility between application versions and persisted formats are operational responsibilities outside the pure checker.

Logs, catalog recovery, atomic persistence, corrupt-file tolerance, and device reinitialization are key support paths. Production runbooks should identify storage locations, safe backup/restore steps, native diagnostic checks, and how to disable problematic add-ons.

## Code References

- `scripts/build-installer.ps1`
- `scripts/fetch-bass.ps1`
- `installer/windows/Liveolator.iss`
- `src/Liveolator.Core/Update/UpdateAvailabilityChecker.cs`
- `src/Liveolator.Online/HttpUpdateManifestSource.cs`
- `src/Liveolator.Core/Audio/AudioReinitCoordinator.cs`
