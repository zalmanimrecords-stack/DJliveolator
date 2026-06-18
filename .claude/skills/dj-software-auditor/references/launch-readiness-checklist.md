# Launch-Readiness Checklist (risk-based)

A go/no-go checklist for shipping a DJ application to users. Score each item **Pass /
At-risk / Fail**, note the **risk**, and mark **blocker?**. The release is **no-go** if any
Critical item is At-risk or Fail. End every review with an explicit go/no-go and the top
blockers.

Priority key: **Critical** = unsafe live / data loss / crash · **High** = core workflow ·
**Medium** = polish · **Low** = nice-to-have.

---

## 1. Audio engine (Critical)

- [ ] No dropouts/xruns with 2 decks + FX at a realistic buffer over 1 hour.
- [ ] No dropouts with the maximum supported deck count + FX + recording.
- [ ] Action→audio latency within target (~<50 ms) and stable under load.
- [ ] Audio thread does NO file IO / allocation / locking (real-time safe).
- [ ] Audio device change (unplug/replug, sample-rate change, OS default switch) recovers
      without crash or permanent silence.
- [ ] Correct sample-rate conversion; no pitch/speed error from SR mismatch.
- [ ] CPU and memory bounded over a multi-hour set (no leak, no climb).

## 2. Live reliability (Critical)

- [ ] 4-hour soak test: zero crashes, zero dropouts.
- [ ] No modal dialog or blocking operation can stop/mute audio mid-set.
- [ ] Music drive removed mid-set: loaded decks keep playing; no crash.
- [ ] Controller hot-plug/unplug during playback: no crash; clean recovery.
- [ ] Network loss during a streamed/cloud session: graceful offline fallback.
- [ ] Crash/force-kill recovery: library, cues, grids, history intact on relaunch.
- [ ] Background safety logging exists for post-mortem (no secrets logged).

## 3. Data safety (Critical)

- [ ] Library/crates/cues/grids/history autosaved; survive crash and upgrade.
- [ ] Metadata writes never corrupt audio files; round-trip verified across formats.
- [ ] Manual grid/cue/key edits are never silently overwritten by re-analysis.
- [ ] Library migration/version upgrade is non-destructive and reversible.
- [ ] Backup/export path exists for the user's library.

## 4. Core DJ features (High)

- [ ] Load + play + cue with no clicks/pops.
- [ ] Beatgrid auto-detection accurate on the reference set + manual correction works.
- [ ] Key detection + Camelot + compatible-key suggestions.
- [ ] 8 hot cues, auto/manual loops, loop roll, beat jump, slip — all working and persisted.
- [ ] Phase-locked sync that holds; clear master; instant manual takeover.
- [ ] Key-lock clean to ±8%.
- [ ] 3-band kill EQ + filter + crossfader (curves) + beat-synced FX, no zipper noise.
- [ ] Master/booth/headphone outputs route correctly; level meters accurate.
- [ ] Recording produces a clean, correct-level file.

## 5. Hardware & controllers (High)

- [ ] Target controllers (e.g. Push 1, CMD STUDIO 2A) plug-and-play.
- [ ] MIDI learn works; mappings persist and are editable.
- [ ] LED/screen feedback matches software state, both directions.
- [ ] Jog/scratch latency acceptable; hot-plug handled.
- [ ] At least one documented, shippable mapping profile per supported device.

## 6. Library & content (High)

- [ ] Fast search/scroll/sort at the largest supported library size.
- [ ] Smart/rule-based playlists; history; prepare/queue list.
- [ ] Supported file formats all load and analyze.
- [ ] Missing-file relocation (bulk) and duplicate detection.
- [ ] Import path from at least one incumbent (Rekordbox XML / Serato / iTunes XML), if claimed.

## 7. UX & onboarding (High/Medium)

- [ ] New user can load two tracks, beatmatch (with sync), and crossfade within minutes.
- [ ] Empty/first-run states are helpful, not blank.
- [ ] Core actions (cue/loop/sync/EQ/crossfade) discoverable.
- [ ] No destructive defaults; beginner-safe defaults that pros can disable.
- [ ] Dark-room legibility; sufficient contrast.
- [ ] Keyboard control of core transport/cue/loop; visible focus; remappable shortcuts.

## 8. Performance & responsiveness (High)

- [ ] UI stays responsive during analysis and heavy library ops (work off the UI thread).
- [ ] Waveform draw is smooth; no lag vs audio.
- [ ] Search/filter latency within target at scale.
- [ ] Startup time reasonable; large-library load doesn't hang.

## 9. Cross-platform & install (High, if multi-OS)

- [ ] Feature parity verified on each OS (no silent feature cliff).
- [ ] Native audio backend (ASIO/CoreAudio) verified per OS.
- [ ] Installer/signing/notarization done (Windows signed, macOS notarized).
- [ ] Clean install on a fresh machine without dev dependencies.

## 10. Streaming / cloud (Critical if shipped)

- [ ] Streaming licensing in place before enabling (legal prerequisite).
- [ ] Streamed tracks analyzed (grid/key) or clearly marked.
- [ ] Caching for set reliability; offline fallback doesn't break a set.
- [ ] Cloud sync conflict handling; clear sync status.

## 11. Legal / compliance (Critical)

- [ ] All third-party libraries license-compatible with distribution (e.g. BASS commercial
      license held; GPL code like Mixxx/qm-dsp NOT copied — ideas only).
- [ ] Required attributions/backlinks present (e.g. data-source attribution rules).
- [ ] No hardcoded secrets in the shipped build.

## 12. Observability & support (Medium)

- [ ] Crash + error logging to a known location (no sensitive data).
- [ ] Version visible in-app; update path defined.
- [ ] Known-issues list and minimum-spec documented for users.

---

## Go / No-go template

> **Verdict:** GO / NO-GO
> **Top blockers (must fix before ship):**
> 1. … (Critical, area, why)
> 2. …
> 3. …
> **At-risk (ship-with-mitigation):** …
> **Confidence:** High / Medium / Low — with the single biggest unknown named.

Rule of thumb: a DJ app is launch-ready when a stranger can perform a full set on it for hours
on real hardware, hit something unexpected (drive yanked, controller unplugged, wrong grid),
and the music never stops.
