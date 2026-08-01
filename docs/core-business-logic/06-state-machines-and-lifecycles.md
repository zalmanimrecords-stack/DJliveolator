# State Machines and Lifecycles

- Last updated: 2026-08-01
- Scope analyzed: Explicit enums, transition guards, immutable state replacements, and event-driven lifecycles.
- Confidence note: High for explicit state; Medium where lifecycle is coordinated in UI composition.

## Autopilot

The engine moves between stopped, running, manually suspended, and re-enabled behavior. `OverrideMode.AutoResume` resumes after the configured bar window; `PauseUntilReenabled` remains suspended. `Stop()` is terminal until a new start. Rule failures disable only the offending rule for the session.

## Live playlist

A queue entry participates in Now/Upcoming and played-oriented behavior represented by `TrackState`. Advance promotes the first upcoming item; end-of-track notification advances or empties Now. Editing Upcoming is deliberately non-disruptive.

## Deck synchronization

`SyncLockState` and `SyncMode` distinguish unlocked, tempo, and phase-oriented behavior. Tempo matching establishes compatible speed; phase correction uses bounded corrections and releases them when aligned. Exact native timing remains adapter-sensitive.

## Studio transport and clips

Transport has play/stop and a moving project position. As time crosses clip boundaries, `StudioArranger` emits Start/Stop events. Automation values are interpolated from keyframes; tempo is resolved from `TempoCurve` keyframes.

## Extension installation

Packages progress from candidate archive through validation/trust checks to installed registry state, then enabled/disabled or uninstalled. Failed validation must not activate content. Missing dependencies or untrusted publishers block ordinary installation.

## Update decision

The pure checker produces an unavailable, available, or skipped-equivalent result from installed version, manifest, and settings. User prompt outcomes are orchestrated by the App and can open download, persist a skipped version, or defer.

## Code References

- `src/Liveolator.Core/Autopilot/AutopilotEngine.cs`
- `src/Liveolator.Core/Playlist/LivePlaylist.cs`
- `src/Liveolator.Core/Audio/Sync/SyncLockState.cs`
- `src/Liveolator.Core/Studio/StudioTransport.cs`
- `src/Liveolator.Media/Extensions/ExtensionInstaller.cs`
- `src/Liveolator.Core/Update/UpdateAvailabilityChecker.cs`
