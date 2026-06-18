# DJ Workflow Principles

How real DJs actually work, and the heuristics that separate software people trust on stage
from software they fight. Use this to ground UX critiques and to judge whether a feature
helps the actual job.

## The cardinal rule

**The music must never stop.** Every design decision is downstream of this. A missing feature
is an inconvenience; a dropout, a crash, or a modal dialog that mutes the room is a
career-affecting failure. Weight live reliability above everything when auditing.

## The phases of DJ work

### 1. Prep (at home, low pressure)
- Import music, analyze BPM/key/grids, fix grids, set hot cues and saved loops, tag/rate,
  build crates and a set/prepare list, organize by energy and harmony.
- **Software job:** make prep fast and batchable; never lose prep work; protect manual edits
  from re-analysis; sync prep to the performance machine/hardware.
- **Audit lens:** Can a DJ grid + cue 200 tracks in an evening without rage? Does prep survive
  reload, export, and version upgrade?

### 2. Selection (live, high pressure, reading the room)
- Find the next track in seconds, judged by BPM compatibility, key (harmonic), energy, and
  vibe. Search, filter, browse crates, see "already played," check related tracks.
- **Software job:** instant search/filter at scale, BPM+key visible, harmonic suggestions,
  history marking, no lag.
- **Audit lens:** Under stage pressure with one hand, can the DJ find and load the right next
  track in under ~10 seconds?

### 3. Cueing & preparation of the incoming track (in headphones)
- Load to the free deck, find the mix-in point (intro/phrase), match tempo (sync or manual
  beatmatch), align phase, pre-set loops/cues, listen in cue.
- **Software job:** clean headphone cue, accurate waveform + phrase markers, reliable
  sync/beatmatch, quantized cues/loops.
- **Audit lens:** Is the cue path obvious? Does sync lock phase and hold? Can the DJ beatmatch
  manually if they prefer (pitch bend / jog)?

### 4. Transition (the moment that matters)
- Blend with EQ (swap basslines), filter sweeps, crossfade, FX (echo/reverb tails, rolls),
  loop the outgoing track to extend, ride the pitch. Often phrase-aligned (32/16-beat).
- **Software job:** zipper-free EQ/filter/crossfader, beat-locked FX, seamless loops, phase
  stability through the blend, instant manual takeover of sync.
- **Audit lens:** Can a full transition happen without a single artifact, and recover
  instantly if the DJ wants manual control?

### 5. Recovery (when something goes wrong)
- Train-wreck (beats drift apart), wrong track loaded, grid was off, controller hiccup. The DJ
  must rescue it live: nudge, re-sync, cut, loop, drop FX to cover.
- **Software job:** never make recovery harder — instant manual control, no fighting sync, no
  blocking prompts, audio keeps playing.
- **Audit lens:** When the grid is wrong mid-transition, does the software help or trap the DJ?

### 6. Post-set
- Save history/tracklist, record the mix, note what worked.
- **Software job:** reliable recording, exportable history, persisted session.

## UX heuristics for DJ software

1. **One-hand, eyes-half-on-it operable.** Core moves (load, cue, loop, EQ, crossfade, sync)
   must be reachable fast, with large targets, in a dark room. No precision mousing mid-set.
2. **No surprises under pressure.** Controls do the same thing every time. Sync doesn't
   re-snap unexpectedly. Quantize doesn't fight an intentional off-grid move.
3. **Never block the music.** No modal dialog, no spinner, no "are you sure?" that can stop or
   mute playback. Confirmations and heavy work happen off the audio path.
4. **Make state legible.** What's playing, what's cued, what's looping, what's synced, what's
   the master, where the phase is — visible at a glance. LEDs/screens match software truth.
5. **Fail loud in prep, fail safe in performance.** Analysis failures should be visible during
   prep; during a set, failures must degrade gracefully without interrupting audio.
6. **Respect manual edits.** Hand-set grids/cues/keys are sacred — never silently overwrite.
7. **Defaults that suit the moment.** Beginner-safe defaults (sync on, quantize on) that pros
   can turn off; nothing destructive by default.
8. **Speed is a feature.** Load time, search latency, control responsiveness, waveform draw —
   all perceived as quality. Lag reads as "unreliable."
9. **Discoverability without clutter.** A beginner should find cue/loop/sync; a pro shouldn't
   be slowed by hand-holding. Progressive disclosure.
10. **Consistency with the idioms DJs already know.** Camelot keys, 3-band EQ, crossfader
    curves, hot-cue colors, beat-jump sizes — match the mental model the incumbents trained.

## Beginner vs pro tension

| Dimension | Beginner needs | Pro needs |
|-----------|----------------|-----------|
| Sync | On by default, "just works" | Trustworthy + instant manual takeover |
| Layout | Few visible controls, guided | Dense, everything mapped to hardware |
| Grids | Auto, hands-off | Manual correction, batch, protected |
| FX | A few good presets | Racks, chains, parameter locks, beat divisions |
| Library | Simple crates, search | Smart lists, history, prep at scale |
| Safety | Can't break anything | Redundancy + recovery tools |

Good software serves both via sensible defaults + progressive depth. When auditing, always ask
"who is this for, and does it still serve the other end of the spectrum?"

## Translating findings into product moves

When you critique a workflow, end with the move, not just the complaint:
- Name the **phase** it hurts (prep/selection/cue/transition/recovery).
- Name the **cost** (seconds lost, risk of train-wreck, lost work, dropout).
- Name the **fix** (specific control/placement/behavior change).
- Name the **priority** (Critical if it risks the set or data; High if it breaks a core phase).
