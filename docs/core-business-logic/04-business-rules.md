# Business Rules

- Last updated: 2026-08-01
- Scope analyzed: Explicit validations, policies, calculations, and handler behavior in Core and Media.
- Confidence note: High unless marked otherwise.

## Enforced rules

- Each `PerformanceActionKind` may be owned by only one registered handler; duplicate ownership fails during dispatcher construction. Unknown actions and handler failures are logged without crashing the performance host.
- Live-queue edits to Upcoming do not interrupt Now. Now cannot be removed through ordinary removal, stale identifiers are ignored, and scheduled skips use the shared beat scheduler.
- `DeckTrackLoader` rejects unreachable files before dispatch. If the target deck is already playing, the requested track is appended instead of replacing audible playback.
- Harmonic-set selection applies Camelot compatibility, BPM constraints/trend, and deterministic ordering/tie behavior according to `HarmonicSetOptions`.
- Autopilot rules must pass trigger, condition, and cooldown checks. Scene choice is constrained to a curated pool and can be deterministic through seeded randomness. A throwing rule is disabled for the session.
- Manual corrections are protected by merge/reanalysis policy unless overwrite is explicitly requested; online metadata is merged rather than blindly replacing stronger local/manual values.
- Controller matching normalizes NoteOn velocity zero as NoteOff, detects binding conflicts, converts absolute/relative encodings, and supports soft takeover to prevent parameter jumps.
- Mixer/deck math clamps control ranges. The two hidden studio decks are not attenuated by the A/B crossfader.
- Track visual programs and `.frktl`/control-skin files undergo explicit validation before use or persistence.
- Extension installation validates paths, dependencies, hashes/signatures, publisher trust, and package structure; developer mode changes the trust posture.
- Update prompts are conservative: malformed versions or manifests yield no offer, skipped versions are suppressed, and only a strictly newer version is available.

## Needs validation

- Native device latency, hardware feedback, and real VST3/GL behavior cannot be guaranteed by pure-Core rules alone.

## Code References

- `src/Liveolator.Core/Actions/PerformanceActionDispatcher.cs`
- `src/Liveolator.Core/Playlist/DeckTrackLoader.cs`
- `src/Liveolator.Core/Playlist/HarmonicSetBuilder.cs`
- `src/Liveolator.Core/Enrichment/MetadataMergePolicy.cs`
- `src/Liveolator.Core/Mapping/SoftTakeover.cs`
- `src/Liveolator.Media/Extensions/ExtensionPackageValidator.cs`
