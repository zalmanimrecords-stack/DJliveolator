using Liveolator.Core.Analysis.Stems;
using Liveolator.Core.Audio.Sync;

namespace Liveolator.Core.Audio;

/// <summary>
/// A two-deck playback engine seam (doc 11): the same load/play-pause/stop operations as
/// <see cref="IAudioPlaybackEngine"/> but addressed per deck slot (A = 0, B = 1), so a single
/// <see cref="DeckActionHandler"/> can drive both decks from slot-tagged actions. Kept separate
/// from the single-deck seam so the existing single-deck path and its tests are untouched; the
/// concrete two-deck engine (and its BASS routing into the software mixer) is a later increment.
/// </summary>
public interface IMultiDeckPlaybackEngine
{
    /// <summary>Number of addressable deck slots.</summary>
    int DeckCount { get; }

    /// <summary>
    /// Raised when a deck slot's track reaches its end during playback (doc 11/22 A4). The event arg is
    /// the slot index. The live-queue audio binding listens for this to auto-advance (or stop when the
    /// queue is dry). Raised off the audio binding's end-of-stream signal, so handlers must be tolerant
    /// of the calling thread; a single-deck engine adapts it to slot 0.
    /// </summary>
    event EventHandler<int>? DeckEnded;

    /// <summary>True while the given deck slot is playing.</summary>
    bool IsPlaying(int slot);

    /// <summary>Load a track into a deck slot, replacing any current one. Does not auto-play.</summary>
    void Load(int slot, string trackPath);

    /// <summary>Start the deck slot, or pause it if already playing. No-op if nothing is loaded.</summary>
    void PlayPause(int slot);

    /// <summary>Stop the deck slot (keeps its loaded track).</summary>
    void Stop(int slot);

    // --- Transport (doc 11): position scrub, pitch/tempo, cue, and per-deck sync/quantize toggles.
    // Driven via DeckSeek/DeckPitch/DeckBpm/DeckCue/DeckSyncToggle/DeckQuantizeToggle actions, never directly.

    /// <summary>Current playback position as a normalized 0..1 fraction of the track (0 if nothing loaded).</summary>
    double Position(int slot);

    /// <summary>The loaded track's length in seconds, or 0 when nothing is loaded. Lets read-ahead
    /// logic (auto-mix placement, doc 11) reason in real time without a native reference.</summary>
    double LengthSeconds(int slot);

    /// <summary>
    /// Move the playhead. When <paramref name="relative"/> is false, <paramref name="position"/> is an
    /// absolute 0..1 fraction; when true it is a signed delta added to the current position. The engine
    /// clamps the result to 0..1. No-op if nothing is loaded.
    /// </summary>
    void Seek(int slot, double position, bool relative);

    /// <summary>
    /// Move the playhead by a signed number of seconds. Used by jog wheels so sensitivity is
    /// independent of track length. The engine clamps the result to the loaded track.
    /// </summary>
    void Jog(int slot, double deltaSeconds);

    /// <summary>Normalized pitch position 0..1 where 0.5 = original tempo (the engine owns the % range).</summary>
    double PitchPosition(int slot);

    /// <summary>
    /// Adjust the deck's pitch/tempo. An absolute <paramref name="value"/> is a 0..1 position
    /// (0.5 = no change); a relative value is a signed delta. The engine maps the normalized position
    /// to its tempo range.
    /// </summary>
    void SetPitch(int slot, double value, bool relative);

    /// <summary>The deck's current audible tempo after pitch/rate adjustment, or 0 when BPM is unknown.</summary>
    double DeckBpm(int slot);

    /// <summary>The minimum BPM reachable through the deck's configured pitch range, or 0 when unknown.</summary>
    double MinimumDeckBpm(int slot);

    /// <summary>The maximum BPM reachable through the deck's configured pitch range, or 0 when unknown.</summary>
    double MaximumDeckBpm(int slot);

    /// <summary>
    /// Set the deck's audible tempo in BPM. The engine clamps it to the configured pitch range and updates
    /// the same rate state used by <see cref="SetPitch"/>.
    /// </summary>
    void SetDeckBpm(int slot, double bpm);

    /// <summary>
    /// Apply a momentary pitch-bend for manual beat-matching: temporarily scale the deck's playback rate by
    /// <c>1 + <paramref name="bendFraction"/></c> (e.g. +0.03 = 3% faster) WITHOUT changing the pitch fader
    /// or nominal BPM, so the deck's phase slides smoothly. Pass 0 to restore the deck's normal rate
    /// (release). A no-op when nothing is loaded or the deck is sync-locked (Sync owns the rate).
    /// </summary>
    void PitchBend(int slot, double bendFraction);

    /// <summary>Jump the playhead to the deck's cue point (defaults to the track start) and pause there.</summary>
    void Cue(int slot);

    /// <summary>
    /// Cue-play preview (CDJ press-and-hold): <paramref name="isPressed"/> true plays from the deck's cue
    /// point (setting the temp cue at the current position when none is set); false returns to the cue and
    /// pauses. Distinct from <see cref="Cue"/> so a single click keeps the back-to-cue behavior.
    /// </summary>
    void CuePlay(int slot, bool isPressed);

    /// <summary>
    /// The deck's analyzed natural tempo (BPM) used as the Sync reference; 0 when unknown. Set when a
    /// track with a known BPM loads so Sync Lock can beatmatch against it (doc 11).
    /// </summary>
    double DeckBaseBpm(int slot);

    /// <summary>Set the deck's natural tempo (BPM) used as the Sync reference. 0 (or negative) = unknown.</summary>
    void SetDeckBaseBpm(int slot, double bpm);

    /// <summary>
    /// The deck's first-beat (downbeat) anchor in seconds from the track start — the within-beat offset
    /// where the analyzed beat grid begins. Used by Quantize/phase-match to align the deck to the shared
    /// grid (doc 11). 0 when unknown.
    /// </summary>
    double DeckFirstBeat(int slot);

    /// <summary>
    /// Set the deck's first-beat anchor (seconds), fed from the track's analyzed <c>BpmResult</c> on
    /// load (like base BPM). Negative = unknown.
    /// </summary>
    void SetDeckFirstBeat(int slot, double firstBeatSeconds);

    /// <summary>
    /// The track's analyzed kick strike times in source-media seconds. Sync uses these as local phase
    /// anchors near the playhead so beat-lock lands on audible kicks, not only a coarse global grid.
    /// </summary>
    IReadOnlyList<double> DeckKickOnsets(int slot);

    /// <summary>
    /// Set the track's analyzed kick strike times in source-media seconds. Invalid values are ignored;
    /// an empty list means the deck falls back to its first-beat grid.
    /// </summary>
    void SetDeckKickOnsets(int slot, IReadOnlyList<double> kickOnsetsSeconds);

    /// <summary>
    /// Set the deck's downbeat (bar-1 "one") anchor in seconds — a confidence-gated analyzed downbeat or
    /// a manual SET ONE, arriving via <c>DeckSetDownbeat</c>. When BOTH decks have one, Quantize/SYNC
    /// phase-match snaps onto the leader's DOWNBEAT instead of just the nearest beat, so engaging sync
    /// can never land beat 3 on the leader's one. 0 (or negative) = unknown → beat-level alignment.
    /// </summary>
    void SetDeckDownbeat(int slot, double downbeatSeconds);

    /// <summary>
    /// True when the deck's analyzed grid is trustworthy enough to PHASE-sync (SYNC-BEHAVIOR-SPEC §7).
    /// When false, Sync tempo-matches only and skips beat/phase alignment (a confident-but-wrong lock on a
    /// bad grid is worse than a tempo-only downgrade). Defaults to true (confident) so a track without
    /// grid-confidence signals — an older catalog, or one not yet analyzed — preserves phase sync.
    /// </summary>
    bool DeckPhaseSyncReady(int slot);

    /// <summary>
    /// Set whether the deck may phase-sync, fed from the track's grid-confidence on load (like the
    /// downbeat). false ⇒ Sync holds tempo-only for this deck; the continuous lock does not phase-correct.
    /// </summary>
    void SetDeckPhaseSyncReady(int slot, bool ready);

    /// <summary>
    /// Beatmatches this deck to the other deck and snaps its analyzed kick/grid phase once.
    /// The resulting audible tempo is retained until the performer changes pitch/tempo again; no
    /// continuous lock is engaged. The matched rate may exceed the manual pitch fader's display range.
    /// </summary>
    void SyncOnce(int slot);

    /// <summary>True while the deck is sync-locked (beatmatched + phase-locked) to the master.</summary>
    bool IsSyncLocked(int slot);

    /// <summary>
    /// Enable or disable sync-lock for the deck. Engaging makes this deck the slave and the other loaded
    /// deck the persistent sync <see cref="SyncMaster"/>; the slave is beatmatched, phase-snapped onto the
    /// master's grid, then held there by the continuous correction loop (<see cref="UpdateSync"/>).
    /// </summary>
    void SetSyncLock(int slot, bool enabled);

    /// <summary>
    /// The deck's sync mode (SYNC-BEHAVIOR-SPEC §4): <see cref="SyncMode.BeatLock"/> (tempo + phase, the
    /// default) or <see cref="SyncMode.TempoOnly"/> (tempo-match, phase left to the DJ). Per-deck; persists
    /// across loads. Only affects behaviour while sync is engaged.
    /// </summary>
    SyncMode DeckSyncMode(int slot);

    /// <summary>
    /// Set the deck's sync mode. When sync is already engaged, switching to <see cref="SyncMode.BeatLock"/>
    /// snaps the phase now (then the loop holds it); switching to <see cref="SyncMode.TempoOnly"/> stops the
    /// phase correction (tempo tracking continues). Never changes the matched tempo.
    /// </summary>
    void SetDeckSyncMode(int slot, SyncMode mode);

    /// <summary>
    /// The deck slot currently acting as the sync master (the reference the slave locks onto), or null
    /// when no deck is synced. The master never has its tempo changed automatically; it also drives the
    /// shared beat clock so the visuals lock to the same grid (doc 03/11).
    /// </summary>
    int? SyncMaster { get; }

    /// <summary>The deck's beat-lock state for the SYNC button / waveform indicator (doc 11/12).</summary>
    SyncLockState SyncState(int slot);

    /// <summary>
    /// Raised whenever a deck slot's <see cref="SyncState"/> TRANSITIONS (Off→Active→Locked→Drifting→
    /// OutOfRange, in any direction) — including the autonomous moves the continuous correction loop makes
    /// with no action dispatched. Lets the SYNC LED / UI indicator follow the live lock state via push,
    /// not a poll. Args: (slot, new state). Raised OFF the engine lock (a handler may do MIDI I/O or marshal
    /// to the UI thread), so it can fire from the clock-pump thread; handlers must be thread-tolerant.
    /// </summary>
    event Action<int, SyncLockState>? SyncStateChanged;

    /// <summary>True while the deck quantizes cue/loop actions to the beat grid.</summary>
    bool IsQuantizeEnabled(int slot);

    /// <summary>Enable or disable beat-quantize for the deck.</summary>
    void SetQuantize(int slot, bool enabled);

    /// <summary>
    /// True while key-lock (master tempo) is engaged: the deck preserves the track's musical pitch as
    /// its tempo changes. When off, pitch follows tempo like a vinyl pitch fader (the default).
    /// </summary>
    bool IsKeyLockEnabled(int slot);

    /// <summary>
    /// Enable or disable key-lock (master tempo) for the deck. When enabled, tempo/pitch changes are
    /// time-stretched so the musical key is preserved. Per-deck state that persists across track loads.
    /// </summary>
    void SetKeyLock(int slot, bool enabled);

    /// <summary>Number of hot-cue slots per deck (valid <see cref="HotCue"/> indices are 0..count-1).</summary>
    int HotCueCount { get; }

    /// <summary>True if the deck's hot-cue at <paramref name="cueIndex"/> is set for the loaded track.</summary>
    bool IsHotCueSet(int slot, int cueIndex);

    /// <summary>
    /// The deck's hot-cue at <paramref name="cueIndex"/> as display state — whether it is set plus its
    /// label, color and "suggested" (auto) flag — so a pad can show the cue's name/color and mark
    /// suggestions. Returns <see cref="HotCueInfo.Unset"/> for an empty slot or when nothing is loaded.
    /// </summary>
    HotCueInfo GetHotCueInfo(int slot, int cueIndex);

    /// <summary>
    /// Trigger a hot-cue: set it at the current position if unset, otherwise jump the playhead to it.
    /// Hot-cues belong to the loaded track and are cleared when a new track loads.
    /// </summary>
    void HotCue(int slot, int cueIndex);

    /// <summary>Clear (delete) the hot cue at <paramref name="cueIndex"/> on the deck and persist the
    /// removal. A no-op when nothing is loaded or the pad is already empty.</summary>
    void ClearHotCue(int slot, int cueIndex);

    /// <summary>
    /// Re-read the deck's hot-cue bank from persistent storage for its currently loaded track, replacing
    /// the in-memory bank. Used by auto-cue placement to surface freshly-written cues without reloading
    /// the track (doc 11/16). A no-op when nothing is loaded or there is no cue store. Tolerant: a store
    /// hiccup leaves the bank as-is rather than failing.
    /// </summary>
    void ReloadHotCues(int slot);

    // --- Loops (doc 11): a beat-length loop repeats a region of the track. Driven via DeckSetLoop.

    /// <summary>The deck's active loop length in beats, or 0 when no loop is active.</summary>
    double LoopBeats(int slot);

    /// <summary>True while the deck is looping a region.</summary>
    bool IsLooping(int slot);

    /// <summary>
    /// Start a beat-length loop on the deck, beginning at the current playhead — or, when a loop is already
    /// running, resize it live to <paramref name="beats"/> beats keeping its in-point fixed (the loop-length
    /// knob path). The beat length is converted to a time region using the deck's base BPM, so it is musically
    /// <paramref name="beats"/> beats long. No-op (and feedback-only) if nothing is loaded or the base BPM is unknown.
    /// </summary>
    void SetLoop(int slot, double beats);

    /// <summary>Clear the deck's active loop (playback continues past the former loop region).</summary>
    void ClearLoop(int slot);

    /// <summary>Halve the active loop length, keeping the in-point fixed (down to the minimum). No-op when not looping.</summary>
    void HalveLoop(int slot);

    /// <summary>Double the active loop length, keeping the in-point fixed (up to the maximum). No-op when not looping.</summary>
    void DoubleLoop(int slot);

    // --- Stems (doc 32 §Phase 2b): a deck loaded with a complete local stem set plays as a 4-stem submix,
    // letting each stem be muted independently. Driven via DeckStemMute, never directly.

    /// <summary>
    /// True when the deck's currently loaded track is playing as a 4-stem submix (the stems gate is on and a
    /// complete local stem set was cached at load). False for a normal single-file deck — the per-stem mute
    /// controls have no effect and the UI disables them.
    /// </summary>
    bool IsStemDeck(int slot);

    /// <summary>
    /// True when <paramref name="kind"/> is muted on the deck. Always false for a single-file deck (no stems)
    /// and reset to false (audible) on every track load, since mute is a per-track transition gesture.
    /// </summary>
    bool IsStemMuted(int slot, StemKind kind);

    /// <summary>
    /// Mute or un-mute one stem of a stem deck (ramped, click-free). A no-op when nothing is loaded or the
    /// deck is a single-file deck. State is per-track and resets to all-audible on the next load.
    /// </summary>
    void SetStemMuted(int slot, StemKind kind, bool muted);

    /// <summary>
    /// Set one stem's volume to a continuous 0..1 level (doc 32 §2b, DJ PRO stem knobs). A no-op when
    /// nothing is loaded or the deck is a single-file deck. Absolute (not a toggle); reset to unity on load.
    /// </summary>
    void SetStemGain(int slot, StemKind kind, double gain);
}
