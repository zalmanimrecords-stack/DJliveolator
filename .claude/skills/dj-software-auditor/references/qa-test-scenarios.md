# QA Test Scenarios — DJ software

Reusable test cases and edge-case banks per subsystem. Use these as starting templates; adapt
preconditions and steps to the actual build. Every case uses the standard structure:

> **Test name · Area · Preconditions · Steps · Expected result · Edge cases · Severity ·
> Automation potential**

Severity: Critical (unsafe live / data loss) · High (core workflow broken) · Medium (friction)
· Low (polish). Automation potential reflects how much can be checked without a human ear.

---

## Decks & playback

### Load and play does not glitch
- **Area:** Decks
- **Preconditions:** Track analyzed, deck empty, output to a monitored device.
- **Steps:** 1) Load track to deck A. 2) Press play. 3) Pause. 4) Press play again. 5) Cue back to start.
- **Expected:** No click/pop on any transition; playhead starts at sample 0; audio matches waveform position.
- **Edge cases:** Load while previous track still playing; load a 30-second track; load a 2-hour DJ mix; load a mono file; load a VBR MP3; load while analysis still running.
- **Severity:** Critical
- **Automation potential:** Medium (transport state automatable; pop detection needs audio capture/RMS analysis).

### Action-to-audio latency
- **Area:** Decks / Audio engine
- **Preconditions:** Controller connected, buffer at typical setting.
- **Steps:** 1) Tap play on hardware. 2) Measure time to audible sound (loopback mic or timestamp).
- **Expected:** Total latency under ~50 ms; consistent across 20 presses.
- **Edge cases:** Under CPU load (4 decks + FX); after a device sample-rate change.
- **Severity:** High
- **Automation potential:** Low (needs audio loopback rig).

### Long-track drift
- **Area:** Decks / Sync
- **Preconditions:** Single 60-min steady-tempo track.
- **Steps:** 1) Play from start. 2) Compare playhead-vs-grid at 1, 30, 60 min.
- **Expected:** No cumulative drift between displayed position, grid, and audio.
- **Edge cases:** 44.1 vs 48 kHz device while track is 44.1; key-lock engaged.
- **Severity:** High
- **Automation potential:** Medium.

---

## Beatgrid & BPM

### Auto-BPM accuracy
- **Area:** Beatgrid
- **Preconditions:** Reference set with known BPMs (steady 4/4, half-time, variable).
- **Steps:** 1) Analyze each. 2) Compare detected vs reference BPM and downbeat.
- **Expected:** Steady 4/4 within ±0.1; downbeat on beat 1; no silent half/double errors.
- **Edge cases:** 87/174 ambiguity; live-recorded set; ambient with weak transient; tempo change mid-track; 3/4 time.
- **Severity:** High
- **Automation potential:** High (compare against a labeled fixture set).

### Manual grid correction persists
- **Area:** Beatgrid
- **Preconditions:** Track with a slightly-off auto grid.
- **Steps:** 1) Set first beat. 2) Adjust grid. 3) Close/reopen library. 4) Re-run analysis.
- **Expected:** Hand edits persist after reload; re-analysis does NOT silently overwrite them.
- **Edge cases:** Bulk re-analyze including edited tracks; export/import library round-trip.
- **Severity:** High
- **Automation potential:** High.

---

## Key & harmonic

### Key detection + compatible suggestions
- **Area:** Key
- **Preconditions:** Reference set with known keys.
- **Steps:** 1) Analyze. 2) Read Camelot value. 3) Request compatible-key matches.
- **Expected:** Matches reference for clear-key tracks; suggestions = same, ±1, relative major/minor, +7 energy.
- **Edge cases:** Modal/atonal track; key change mid-track; no override available.
- **Severity:** Medium
- **Automation potential:** High (labeled fixtures + Camelot math unit tests).

---

## Cues, loops, beat jump, slip

### Hot cues persist and are sample-accurate
- **Area:** Cues
- **Preconditions:** Track loaded.
- **Steps:** 1) Set 8 hot cues with labels/colors. 2) Trigger each. 3) Reload track.
- **Expected:** Each cue jumps to exact sample, audible on press, all persist after reload.
- **Edge cases:** Cue at sample 0; cue at last sample; cue during loop; rapid re-trigger.
- **Severity:** High
- **Automation potential:** Medium.

### Seamless loop
- **Area:** Loops
- **Preconditions:** Beatgridded track.
- **Steps:** 1) Set a 4-beat auto loop. 2) Let it cycle 8 times. 3) Halve, then double loop length. 4) Exit loop.
- **Expected:** No click at loop boundary; halve/double stay beat-aligned; exit resumes in phase.
- **Edge cases:** Loop across a tempo change; 1/32-beat loop roll; loop at track end; resize while playing.
- **Severity:** High
- **Automation potential:** Medium (boundary click needs audio analysis).

### Slip mode resumes correctly
- **Area:** Slip
- **Preconditions:** Slip mode on, track playing.
- **Steps:** 1) Trigger a loop roll / scratch for 4 beats. 2) Release.
- **Expected:** Playback resumes where it *would* have been, in phase.
- **Edge cases:** Slip + hot cue; slip across loop; slip while synced.
- **Severity:** Medium
- **Automation potential:** Medium.

---

## Sync, tempo, pitch, key-lock

### Phase sync holds
- **Area:** Sync
- **Preconditions:** Two beatgridded tracks, similar BPM.
- **Steps:** 1) Play deck A. 2) Sync deck B. 3) Let run 5 min. 4) Check phase alignment.
- **Expected:** Beats stay phase-locked, not just tempo-matched; no drift.
- **Edge cases:** Sync across half/double tempo; sync onto a track with a grid error; change master mid-mix; nudge while synced; sync 3–4 decks.
- **Severity:** Critical
- **Automation potential:** Medium.

### Key-lock artifacts
- **Area:** Pitch / key-lock
- **Preconditions:** Key-lock engaged.
- **Steps:** 1) Pitch ±8%. 2) Listen for warble on vocals/sustained tones.
- **Expected:** Clean within ±8%; graceful degradation beyond.
- **Edge cases:** Extreme pitch; key-lock toggle while playing; key-lock + sync.
- **Severity:** Medium
- **Automation potential:** Low (perceptual).

---

## Mixer, EQ, FX, crossfader

### EQ kill and gain staging
- **Area:** Mixer
- **Preconditions:** Track at unity gain.
- **Steps:** 1) Kill low/mid/high one at a time. 2) Watch master meter. 3) Boost gain to clip and back.
- **Expected:** Full kill on each band; no clipping at unity; meters accurate; no zipper noise on any control.
- **Edge cases:** All bands killed (silence); rapid fader moves; gain + EQ + filter together.
- **Severity:** High
- **Automation potential:** Medium.

### Beat-synced FX timing
- **Area:** FX
- **Preconditions:** Beat-synced delay/echo on a gridded track.
- **Steps:** 1) Engage FX at 1/4 beat. 2) Change beat division. 3) Wet/dry sweep.
- **Expected:** FX timing locks to the grid; division changes are musical; dry path unaffected at 0% wet.
- **Edge cases:** FX during tempo change; FX with no grid; stacking multiple FX.
- **Severity:** Medium
- **Automation potential:** Medium.

### Crossfader curve and assignment
- **Area:** Crossfader
- **Preconditions:** Two decks playing.
- **Steps:** 1) Sweep crossfader. 2) Switch curve (smooth/sharp). 3) Reassign channels.
- **Expected:** Curve behaves as labeled; no audio leak at full ends; assignment respected.
- **Edge cases:** Hamster/reverse mode; both channels on one side; cut-in point.
- **Severity:** Medium
- **Automation potential:** High.

---

## Library, import, metadata

### Search/scroll at scale
- **Area:** Library
- **Preconditions:** 50k-track library.
- **Steps:** 1) Type incremental search. 2) Sort by BPM/key. 3) Scroll rapidly.
- **Expected:** Results update within ~100 ms; no UI freeze; sort correct.
- **Edge cases:** Unicode/emoji in tags; 200k tracks; empty library; search during analysis.
- **Severity:** High
- **Automation potential:** High.

### Missing files and duplicates
- **Area:** Import
- **Preconditions:** Library referencing files on a removable drive.
- **Steps:** 1) Unplug drive. 2) Browse + try to load. 3) Replug to a new path. 4) Run relocate. 5) Run dup scan.
- **Expected:** Missing files clearly flagged (not silently dropped); bulk relocate works; duplicates detected without false merges.
- **Edge cases:** Same filename different content; moved-not-deleted; network drive timeout; cues/grids survive relocate.
- **Severity:** High
- **Automation potential:** Medium.

### Metadata write safety
- **Area:** Metadata
- **Preconditions:** Editable tag fields.
- **Steps:** 1) Edit BPM/key/comment/rating. 2) Reopen the file in another app.
- **Expected:** Tags written without corrupting the file or audio; round-trips correctly.
- **Edge cases:** Read-only file; FLAC vs MP3 vs AIFF; huge embedded artwork.
- **Severity:** High
- **Automation potential:** Medium.

---

## Audio engine & stability

### No dropouts under load
- **Area:** Audio engine
- **Preconditions:** 4 decks + FX + recording.
- **Steps:** 1) Play all. 2) Trigger FX + loops + scrub simultaneously for 10 min.
- **Expected:** No xruns/dropouts; CPU bounded; audio thread never blocks on IO.
- **Edge cases:** Smallest buffer; switch audio device mid-playback; USB controller hot-plug; laptop on battery/power-saver.
- **Severity:** Critical
- **Automation potential:** Medium (xrun counters/telemetry).

### Device change recovery
- **Area:** Audio engine
- **Preconditions:** Playing on USB interface.
- **Steps:** 1) Unplug interface mid-track.
- **Expected:** App survives; clear prompt; resume on reconnect; no crash/lockup.
- **Edge cases:** Sample-rate mismatch on new device; default-device switch by OS.
- **Severity:** Critical
- **Automation potential:** Low.

---

## Controller / MIDI / HID

### MIDI learn + LED feedback
- **Area:** Controller
- **Preconditions:** Controller connected, learn mode available.
- **Steps:** 1) Learn a pad → hot cue. 2) Trigger from hardware. 3) Watch LED. 4) Change state in software.
- **Expected:** Mapping captured (not hardcoded); LED reflects true software state both directions; jog latency low.
- **Edge cases:** Hot-plug mid-set; two controllers at once; knob takeover/soft-pickup; unplug during use.
- **Severity:** High
- **Automation potential:** Low (hardware in loop).

---

## Recording & broadcasting

### Clean master recording
- **Area:** Recording
- **Preconditions:** Recording target set.
- **Steps:** 1) Record a 10-min mix with FX/scratch. 2) Stop. 3) Inspect file.
- **Expected:** No dropped samples; correct levels; chosen format; playback matches what was heard.
- **Edge cases:** Disk near-full; very long (3-hour) recording; record while broadcasting; pause/resume.
- **Severity:** High
- **Automation potential:** Medium.

---

## Live-reliability scenarios (run as a suite before any release)

1. **4-hour soak:** play continuously, every 10 min do a transition with FX. Expect zero crashes, zero dropouts, bounded CPU/memory (no leak).
2. **Hostile hardware:** unplug/replug controller and audio interface repeatedly during playback.
3. **Drive yanked:** pull the music drive mid-set; app must keep playing loaded decks and not crash.
4. **Network death:** kill wifi during a streamed/cloud session; expect graceful offline fallback.
5. **Crash recovery:** force-kill the app; on relaunch, library/cues/grids/history intact, fast recovery.
6. **No modal blocks the music:** verify no dialog can stop or mute audio mid-set.

---

## Cross-cutting edge-case bank

- Zero-length / corrupt / DRM'd file · 0 BPM / absurd BPM · silence-only track · clipping
  source · extreme sample rates (96/192 kHz) · mono and surround files · non-ASCII/emoji
  metadata · 200k-track library · simultaneous control storms · rapid repeated triggers ·
  first-run empty state · upgraded-from-old-version library · two app instances · OS sleep/
  wake during playback · timezone/locale on history timestamps.
