# 33 — DJ PRO Controls Reference

- **Purpose:** a button-by-button / knob-by-knob legend for the DJ PRO tab's deck and mixer surface —
  what each control does, not whether it is reachable (that fact belongs to
  [`core-business-logic/06`](core-business-logic/06-ui-feature-coverage.md#coverage-matrix)).
- **Scope:** `DjProDeckView` (×2), `DjProHotCueStripView` (×2), `DjProLoopView` (×2), `DjProSyncView`
  (×2), `DjProTrackBrowseView` (×2), `DjProMixerView`, `DeckFxRackView` (×2), `DeckStemRackView` (×2),
  and the shared waveform/zoom strip above them. Excludes the LIVE tab's own deck/mixer views
  (`DjDeckView`, `DjMixerView`, `Features/Live/Modules/MixerView`), which are separate controls with
  their own layout — and, as of the 2026-09-26 core-business-logic refresh, are no longer reachable
  from any shell tab at all ([`06`](core-business-logic/06-ui-feature-coverage.md#called-out-explicitly)).
- **Source of truth:** `src/Liveolator.App/Features/Dj/DjProView.axaml`, `DjProDeckView.axaml`,
  `DjProHotCueStripView.axaml`, `DjProLoopView.axaml`, `DjProSyncView.axaml`,
  `DjProTrackBrowseView.axaml`, `DjProMixerView.axaml`, `DeckFxRackView.axaml`,
  `DeckStemRackView.axaml` — every description below is taken from that XAML's bindings and its own
  `ToolTip.Tip` text, not paraphrased from memory.
- **Last validated:** 2026-09-26 (against `feat(dj-pro): move FX, hot cues, loop, sync and track
  browse above the decks`, the current working tree; `DeckFxRackView.axaml` and
  `DeckStemRackView.axaml` are unchanged from the previous pass — only their position in the layout
  moved).
- **Maintenance:** this is a live UI reference, not a design-decision record — re-validate it whenever
  one of the source files above changes shape (a control added/removed/relabelled/moved), not on a
  fixed schedule. `docs/core-business-logic/07-doc-inventory-and-status.md` tracks this file.
- **Related:** [DJ PRO reachability](core-business-logic/06-ui-feature-coverage.md) ·
  [Deck A/B design record](11-deck-ab-pro-dj.md) · [sync contract](SYNC-BEHAVIOR-SPEC.md)

## Layout

Both decks share the same set of view templates, so A and B are control-for-control identical — only
the deck's colour identity and `DeckId` differ. Each deck column stacks, top to bottom:

1. A shared **rack row** (`UniformGrid`, one row): `DeckStemRackView` (hidden unless
   `StemsFeature.IsEnabled` — when hidden, the row splits between just the other two), `DeckFxRackView`,
   `DjProHotCueStripView`.
2. A second row: `DjProLoopView`, `DjProSyncView`, `DjProTrackBrowseView`.
3. `DjProDeckView` itself — now just the header, transport (CUE/PLAY), tempo (BPM + nudge), musical
   key, KEY LOCK, pitch BEND, and the jog wheel. Hot cues, loop, sync and track browse used to live
   inside this view; they were pulled out into their own panels above it.

`DjProMixerView` sits between the two deck columns. A shared dual waveform strip with one zoom knob
sits above all three columns.

## Waveform strip

| Control | Binding | Behaviour |
| --- | --- | --- |
| Dual waveform (A over B) | `DeckWaveform`, `DataContext="Decks.DeckA"` / `"Decks.DeckB"` | Read-only scrolling waveform per deck, folded layout. |
| ZOOM knob | `Decks.WaveformZoom` | Zooms both waveforms together (one knob, not per-deck). |

## Hot cues (`DjProHotCueStripView`, ×2 — in the rack row, beside FX)

| Element | Binding | Behaviour |
| --- | --- | --- |
| 4 hot-cue pads (one row) | `VisibleHotCues` → `HotCuePadViewModel.TriggerCommand` | Shows only the **active bank's** four pads (not a fixed 2×4 of 8, as the previous layout showed) — jumps to the cue if `IsSet`, else sets a new one there. Disabled when `!IsEnabled`. Bottom bar shows the cue's colour when `IsSet`; the pad dims (`suggested` style) when `IsAuto` (an auto-detected cue). Tooltip = `CueLabel`. |

Cues 5–8 (bank B) have **no visible toggle in this strip** — the view's own comment says they "stay
reachable from MIDI/hardware via `ToggleHotCueBankCommand`" — so switching banks by mouse alone is not
currently possible from DJ PRO.

## LOOP (`DjProLoopView`, ×2 — second row)

| Element | Binding | Behaviour |
| --- | --- | --- |
| Loop length button (label = `LoopLengthLabel`, e.g. "1 BAR") | `LoopCommand`, enabled by `CanLoop` | Arms a loop of the shown length. Lit while `IsLooping`. |
| ✕ (loop exit) | `ExitLoopCommand`, enabled only while `IsLooping` | Releases the active loop. |

## SYNC (`DjProSyncView`, ×2 — second row, beside LOOP)

| Element | Binding | Behaviour |
| --- | --- | --- |
| SYNC | `SyncCommand`, enabled by `CanSync` | **Sync Lock** — matches tempo *and* phase to the other deck. Green (`locked` style) when `IsSyncLockedTight` — fully phase-locked, "in the pocket". Lit (`on`) while `IsSync` but not yet tight. Red with a ✕ (`cantsync`) when `IsSyncOutOfRange` — tempo relationship is outside what SYNC can bridge. Tooltip: *"Sync Lock — match tempo + phase to the other deck (green = locked in the pocket)."* |
| T·SYNC | `TempoSyncCommand`, enabled by `CanSync` | **Tempo Sync** — matches tempo only; the performer rides the phase by hand. Same red/✕ treatment when `IsTempoSyncOutOfRange`. Tooltip: *"Tempo Sync — match tempo only; you ride the phase by hand."* |
| "◆ GRID UNCERTAIN · TEMPO-ONLY" (text, no button) | `IsGridUncertain` | Shown instead of hidden when the analyzed beatgrid is low-confidence, explaining why SYNC on this deck is tempo-only (no phase lock) until the track is re-analyzed. |

## TRACK browse (`DjProTrackBrowseView`, ×2 — second row, beside SYNC)

| Element | Binding | Behaviour |
| --- | --- | --- |
| ◀ | `PrevCommand` (injected by `DjProView` per deck) | Previous track in the shared browser list → loads (or queues) onto **this** deck only. Never interrupts a playing deck. |
| ▶ | `NextCommand` | Same, next track. |

## Deck panel (`DjProDeckView`, ×2 — the third row)

### Header

| Element | Binding | Behaviour |
| --- | --- | --- |
| A / B badge | `DeckId` | Deck identity; coloured per deck (`DeckBIdentity` for B). |
| MASTER chip | `IsSyncMaster` | Shown only when this deck is the sync reference the other deck is locked to. Tooltip: *"This deck is the sync master — the other deck is locked to it."* |
| Title / meta | `Title`, `Meta` | Loaded track name and metadata line. |
| Elapsed / remaining | `ElapsedText`, `RemainingText` | Time readout, top-right of the deck. |

### Transport

| Element | Binding | Behaviour |
| --- | --- | --- |
| CUE | `CueCommand`, enabled by `CanCue` | Cue point control. |
| ▶ (PLAY) | `PlayPauseCommand` | Play / pause. Lit (`Classes.on`) while `IsPlaying`. |

### Tempo

| Element | Binding | Behaviour |
| --- | --- | --- |
| BPM readout | `Bpm` | Live BPM, formatted to one decimal. Turns green/bold (`matched` style) when `IsBpmMatched`. |
| octave tag (½ / ×2) | `BpmOctaveLabel`, shown when `HasBpmOctaveLabel` | Marks a half-time/double-time beatmatch to the other deck, so e.g. 70 vs 140 BPM reads as a deliberate octave lock, not a broken sync. |
| − | `NudgeLeftCommand`, enabled by `IsBpmEnabled` | Fine tempo nudge, **−0.1 BPM**. |
| + | `NudgeRightCommand`, enabled by `IsBpmEnabled` | Fine tempo nudge, **+0.1 BPM**. |

### Musical key, KEY LOCK and BEND

| Element | Binding | Behaviour |
| --- | --- | --- |
| Musical key readout | `TrackKey`, shown when `HasTrackKey` | The track's detected key (e.g. Camelot code), left-aligned in the row above KEY LOCK. |
| KEY LOCK | `KeyLockCommand`, enabled by `CanSync` | Locks the musical key (master tempo) so tempo changes don't shift pitch. Lit while `IsKeyLock`. Tooltip: *"Key lock (master tempo)."* |
| BEND ◀ | `NudgeBendDownCommand`, enabled by `CanPitchBend` | Momentary pitch-bend slower — manual beatmatch nudge. |
| BEND ▶ | `NudgeBendUpCommand`, enabled by `CanPitchBend` | Momentary pitch-bend faster. |

### Jog wheel

| Element | Binding | Behaviour |
| --- | --- | --- |
| `Jog` platter | `Progress`, `SeekCommand`, `BendCommand`/`BendReleaseCommand`, `KickPeaks`, `IsKickActive`/`IsSpinning` = `IsPlaying` | Tooltip: *"Playing: drag to pitch-bend. Paused: drag to scrub/cue."* The ring tracks playback progress; the centre pulses with detected kick transients while playing. |

## STEMS rack (`DeckStemRackView`, ×2 — in the rack row, hidden unless `StemsFeature.IsEnabled`)

| Knob | Binding | Behaviour |
| --- | --- | --- |
| DRUMS | `Drums.Value` | Per-stem submix level for the drums stem. |
| BASS | `Bass.Value` | Per-stem submix level for the bass stem. |
| VOX | `Vocals.Value` | Per-stem submix level for the vocals stem. |
| OTHER | `Other.Value` | Per-stem submix level for the remaining (melody/harmony) stem. |

All four are enabled only when `IsAvailable` — a stem-separated deck is actually loaded; otherwise the
engine no-ops for a single-file track. Seeded to unity gain. Whether this rack is visible at all is
gated by `StemsFeature.IsEnabled` (`LIVEOLATOR_STEMS=1`) — see
[`core-business-logic/06`](core-business-logic/06-ui-feature-coverage.md) for the current
`Configuration only` reachability status of stems generally.

## FX rack (`DeckFxRackView`, ×2 — in the rack row, always visible — no FX-mode button)

| Knob | Binding | Behaviour |
| --- | --- | --- |
| CUT | `Cutoff.Value`, default fully open | Moog low-pass filter cutoff. |
| RES | `Resonance.Value`, default 0 | Moog filter resonance. |
| PHASE | `Phaser.Value`, default 0 | Phaser dry↔wet. |
| VERB | `Reverb.Value`, default 0 | Reverb dry↔wet. |

Each knob drives the deck's realtime FX rack instance directly via `AudioFxSetParameter` — there is no
separate load/unload/bypass/preset control in DJ PRO (see the `Partial` / `Internal only` rows for
audio effects in [`core-business-logic/06`](core-business-logic/06-ui-feature-coverage.md)).

## Mixer (`DjProMixerView`, one shared instance between the two decks)

| Control | Binding | Behaviour |
| --- | --- | --- |
| RST (per channel) | `ChannelA.ResetEqCommand` / `ChannelB.ResetEqCommand` | Resets that channel's HI/MID/LOW EQ to flat. |
| HI / MID / LOW knobs (per channel) | `Channel{A,B}.Eq{High,Mid,Low}.Value` | 3-band channel EQ. |
| FLT knob (per channel) | `Channel{A,B}.Filter.Value` | Per-channel filter. |
| CUT knob (centre, 3 detents) | `EqCut.Value`, label `EqCut.ModeLabel` | Mixer-wide EQ cut *depth* (how hard the EQ bites), not per-channel: cycles EQ / DEEP / KILL. |
| Channel fader (×2, vertical) | `ChannelGainA.Value` / `ChannelGainB.Value`, level meter `LevelA`/`LevelB` | Channel volume, dB-scaled (+6 … −∞ scale shown alongside). |
| A / B snap buttons | `CrossfadeToACommand` / `CrossfadeToBCommand` | Snaps the crossfader fully to that side. |
| Crossfader (horizontal) | `Crossfader.Value` | A↔B blend. |
| CUE (per channel, under its fader) | `CueACommand` / `CueBCommand`, lit by `IsCueA`/`IsCueB` | Toggles that channel into the headphone/PFL monitor bus. |
| AUTO (between the two CUE buttons) | `AutoCrossfadeCommand`, lit by `IsAutoCrossfading`, red flash `IsAutoCrossfadeRefused` | Moves the crossfader from its side to the other side over MIX SEC (the centre counts as the A side, so it goes to B). Always takes the full MIX SEC, even from partway. Press again to stop where it is; touching the crossfader takes over. Refused, with a red flash, when the deck it would fade into is not playing. A hardware crossfader must then pass the new position before it moves the mix (one-off soft takeover). |
| LEVEL knob | `CueLevel.Value` | Headphone cue monitor volume. |
| MIX SEC knob (between LEVEL and MIX) | `AutoCrossfadeTime.Value`, caption `AutoCrossfadeTimeLabel` | AUTO crossfade time, 0–20 s in 1 s steps. Defaults to 10 s; double-click resets it to 10. Saved on exit. |
| MIX knob | `CueMix.Value` | Blends the headphone monitor between cue-only and master. |

## Notes for future maintenance

- This document intentionally does **not** restate reachability, hardware-mapping or engine wiring —
  those are owned by `core-business-logic/06`, `docs/05-controller-mapping-engine.md` and
  `docs/04-performance-action-system.md` respectively. It only answers "what does this specific
  control on screen do."
- If a control's tooltip text changes in the XAML, prefer updating this table to match the tooltip
  verbatim (it is the developer's own authoritative one-line description) over re-deriving wording
  from the view-model.
- Hot cues, loop, sync and track browse are now separate view files from the deck panel itself
  (`DjProHotCueStripView`, `DjProLoopView`, `DjProSyncView`, `DjProTrackBrowseView`) — when re-validating,
  check all five deck-area files, not just `DjProDeckView.axaml`.
- The LIVE tab's own deck/mixer views (`DjDeckView.axaml`, `DjMixerView.axaml`,
  `Features/Live/Modules/MixerView.axaml`) are out of this document's scope, and as of the 2026-09-26
  refresh are confirmed unreachable from any shell tab — see
  [`core-business-logic/06`](core-business-logic/06-ui-feature-coverage.md#called-out-explicitly).
