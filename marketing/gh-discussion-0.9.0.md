Liveolator 0.9 shipped this week, closing out three releases that were mostly about one thing: making the beat grid, the tempo and the mix render tell the truth. If you have been watching this repo and wondering where a contribution would actually land, the end of this post is for you.

**Download (free, Windows):** https://liveolator.zalmanim.com
**Full changelog:** https://liveolator.zalmanim.com/changelog/

## What shipped in 0.7 → 0.9

**The beat-grid phase anchor moved to the low band.** Liveolator was fitting the grid against the broadband onset envelope, which on kick-and-hat music lands on the off-beat — two synced decks could sit a third of a beat apart and flam on every blend. The anchor now comes from the bass band, where the kick actually is.

**SYNC gates phase-lock on grid confidence.** Below the floor it matches tempo only and says so on screen, rather than locking confidently to the wrong place. A grid the analyzer cannot vouch for is treated as unknown, not assumed good. You can also nudge a grid by ear in milliseconds without re-analyzing the track or losing its confidence score.

**Key detection.** A flaw in the chroma analysis was collapsing most of a library onto the same few keys. On a real test library, correct keys went from 2 in 10 to 8 in 10. Half-tempo misreads (a 125 BPM track reading 168) are fixed.

**Warped clips land on the beat.** The time-stretcher was placing transients up to 10 ms early, by an amount that shifted with the tempo, so two decks stretched by different amounts could flam. That offset is now measured and corrected.

**The set tempo can travel.** A set is no longer pinned to a single number. Each pair of records meets at its own midpoint and the tempo walks between them — a 129 BPM record that needed 8.5% to reach a fixed 140 now needs about 2%. The tempo only moves while a record is playing alone, never across a blend, and the move is spread across that whole stretch.

**Set building reports rather than drops.** Every track that fails to reach the timeline comes back with the real reason. A blend over a quiet floor is reported instead of refused — dropping those was quietly cutting sets short (one case took a 14-track set down to 4). The export gate judges a mix before it renders and refuses it with the fix: a clip at the wrong tempo, a clip left at unity gain, a blend clamped too short. Offline renders report per-window holes so a silent stretch cannot pass as finished.

**Structure detection runs in-process** off a novelty curve — intro, builds, drops, outro, with no Python and no model download.

**Plus:** AAC/M4A decode and render, library scans that save each track as they go (a dropped network drive no longer costs hours of analysis), incremental per-folder scanning, and exports that keep their stereo image.

## How the codebase is laid out

If you are reading the source for the first time:

- **`Liveolator.Core` is pure C#** — no UI, no native, no platform IO. Beat clock, actions, mapping, playlist, visual scene model. The entire rule set is unit-testable with no hardware attached, and that boundary is deliberate.
- **Everything goes through one dispatcher.** Hardware, UI, automation and AI agents all emit the same serializable `PerformanceAction`; engines are driven only through the dispatcher and never call each other. One handler owns each action kind.
- **The audio↔visual link is one shared beat clock.** That is the whole point of the project — the visuals are beat-synced by construction rather than by hand.
- **Stack:** .NET 8 + Avalonia (UI), Silk.NET/OpenGL + SkiaSharp (GLSL effects on textures), BASS/ManagedBass (audio), RtMidi.Core (MIDI), FFmpeg CLI (video/camera decode).
- **`Liveolator.Mcp`** is an MCP server: an external AI agent can query the catalogue, analyze tracks, build a harmonic and tempo-matched set, and render a preview of every transition.
- 1,000+ passing tests. `docs/core-business-logic/00-project-context.md` is the validated map of what exists today.

## Where a contribution would land

- **The VJ authoring screen.** The GL compositor, the layer model and the beat-reactive generators all work — the UI to drive them is open canvas. Highest-leverage thing in the repo by a distance.
- **macOS.** The code is platform-agnostic by design — CI builds and runs the full suite on `macos-latest` as well as `windows-latest`, and `PortablePath` exists so a catalog full of Windows and UNC paths still resolves on a Mac. It wants someone to take it the last mile and ship a build.
- **Controller profiles.** MIDI-learn is built and profiles are data, so supporting a new device does not mean touching engine code (`docs/05-controller-mapping-engine.md`).
- **Visual add-ons.** Generators and GLSL effects plug in through a documented seam (`docs/26-visual-addon-standard.md`), with a built-in VU meter as the reference implementation.
- **Keylock** on pitch changes.

Most of the common extension points have a design doc of their own — performance actions (`docs/04`), the visual engine (`docs/08`), controller mapping (`docs/05`), the beat engine (`docs/03`), the playlist engine (`docs/09`) and the MCP interface (`docs/17`) — so a first change usually does not require understanding the whole app.

## Licence

GPLv3+, and it stays that way. The BASS native libraries are not vendored: they are fetched from un4seen at build time and used under un4seen's own licence, with a GPLv3 §7 additional permission covering the combination (`LICENSE-EXCEPTION.txt`). BASS is free while the product using it is free — anyone who sells a derivative needs their own licence from un4seen.

There are now five issues tagged [`good first issue`](https://github.com/zalmanimrecords-stack/DJliveolator/labels/good%20first%20issue) with the call sites, the reasoning and the verification steps written out:

- #8 — swap the last eleven `System.IO.Path` calls in Core over to `PortablePath` (mechanical, one small new helper)
- #9 — add a CI guard so they cannot come back
- #10 — two design docs still describe NAudio and projectM, neither of which the project uses
- #11 — clear five High-severity transitive dependency advisories
- #12 — write a visual add-on: a beat-synced GLSL effect or generator, shaders and JSON only, no C#

Issues, ideas and PRs all welcome. If you are picking something non-trivial, open an issue first so we can talk through the approach.
