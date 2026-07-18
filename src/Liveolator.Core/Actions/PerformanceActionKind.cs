namespace Liveolator.Core.Actions;

/// <summary>
/// The complete vocabulary of performance intents. Every input source — UI, Push, DJ
/// controller, keyboard, autopilot — expresses what it wants as one of these kinds, so all
/// sources drive the engines through the single dispatcher (doc 04, the action-layer
/// principle of doc 00). The enum stays one cohesive type on purpose; routing of each kind
/// to its owning engine lives in handlers, never in a giant switch.
/// </summary>
public enum PerformanceActionKind
{
    // Transport
    TransportStop,

    // Beat
    BeatTapTempo,
    BeatLock,
    BeatUnlock,
    BeatHalfTempo,
    BeatDoubleTempo,
    BeatNudgeForward,
    BeatNudgeBackward,
    BeatResetGrid,
    BeatSetDownbeat,

    // Visual (compositor model — doc 08; no projectM presets)
    VisualLoadScene,
    VisualSelectBank,
    VisualSetMacro,
    VisualToggleLayer,
    VisualSetLayerOpacity,
    VisualLaunchClip,
    VisualBlackout,
    VisualToggleStrobe,
    VisualTransitionNow,
    VisualTransitionNextBeat,
    VisualTransitionNextBar,

    // Deck / DJ (doc 11) — driven only via actions
    DeckLoadTrack,
    DeckPlayPause,
    DeckCue,
    DeckHotCue,
    DeckSetLoop,
    DeckSeek,
    DeckPitch,
    DeckBpm,
    /// <summary>Relative BPM nudge: Value is the signed delta in BPM (e.g. +0.1 or -0.1).
    /// Clamped to the engine's ±8% range. Use for manual beat-sync fine-tuning.</summary>
    DeckBpmNudge,
    DeckSyncOnce,
    DeckQuantizeToggle,
    DeckSetFirstBeat,

    // Mixer (doc 11)
    MixerCrossfade,
    MixerChannelGain,
    MixerEqBand,
    MixerFilter,
    MixerCueToggle,
    MixerCueLevel,
    MixerCueMix,

    // Audio effects (VST3 host/racks)
    AudioFxLoad,
    AudioFxUnload,
    AudioFxMove,
    AudioFxToggleBypass,
    AudioFxSetParameter,
    AudioFxLoadPreset,

    // Playlist
    PlaylistInsertTrackNext,
    PlaylistMoveTrack,
    PlaylistRemoveFutureTrack,
    PlaylistSkipOnNextBar,

    /// <summary>
    /// Relative jog-wheel motion. Value is a signed fraction of one physical wheel revolution;
    /// the deck handler converts it to a time delta using the current playing/paused sensitivity.
    /// </summary>
    DeckJog,

    /// <summary>Toggle persistent deck tempo/phase synchronization to the other deck.</summary>
    DeckSyncToggle,

    // Appended to preserve the serialized numeric values of existing action kinds.
    VisualSetLayerSource,

    /// <summary>
    /// Loads a controllable generator preset (doc 28) onto a layer: Argument is the preset id, Slot is
    /// the target layer. The handler expands the preset into the generator source + its ≤5 controllable
    /// macros, which are then driven by <see cref="VisualSetMacro"/> from UI knobs / external controllers.
    /// </summary>
    VisualLoadPreset,

    /// <summary>
    /// Appends a track to a deck's live queue without touching what is playing: Slot is the deck
    /// (A = 0, B = 1), Argument is the track path. Emitted instead of <see cref="DeckLoadTrack"/>
    /// when the target deck is playing, so a load can never cut off the floor's audio — the queued
    /// track plays when the current one ends (doc 09/11).
    /// </summary>
    PlaylistAppendTrack,

    /// <summary>
    /// Sets the computer's master output volume (the OS system volume, affecting every application),
    /// not the app's own mix. Value is the absolute 0..1 level for <see cref="ActionInputMode.Absolute"/>
    /// or a signed delta for <see cref="ActionInputMode.Relative"/>. Owned by the platform system-volume
    /// seam (<c>ISystemVolumeController</c>); a no-op on hosts where the OS volume cannot be controlled.
    /// </summary>
    SystemMasterVolume,

    /// <summary>
    /// Toggle key-lock (master tempo) for a deck (Slot = A/B). When on, tempo changes preserve the
    /// track's musical pitch (time-stretch); when off, pitch follows tempo like a vinyl fader (the
    /// default). Per-deck state that persists across track loads (doc 11; roadmap N4 / H1).
    /// </summary>
    DeckKeyLockToggle,

    /// <summary>
    /// Toggle recording of the live master mix to a clean WAV file (roadmap X2). Captures the
    /// post-limiter master (the exact signal the house hears) without affecting playback. Owned by
    /// the <c>IMasterRecorder</c> seam; the handler holds the on/off latch and reports it back so a
    /// REC button / LED reflects the true capture state. Unavailable when no realtime engine is up.
    /// </summary>
    MasterRecordToggle,

    /// <summary>
    /// Set the mixer-wide EQ cut-depth mode (doc 11) — how deep every channel's EQ band-cut goes
    /// (EQ/DEEP/KILL, see <see cref="Mixer.EqCutMode"/>). With <see cref="Mixer.EqCutMode"/> set via
    /// <see cref="PerformanceAction.Argument"/> the handler selects that mode absolutely; with no
    /// argument it cycles to the next (progressively coarser) mode — the single global mixer button.
    /// The handler recomputes and re-pushes every channel's EQ coefficients and reports the active
    /// mode back so the button face stays in sync.
    /// </summary>
    MixerEqCutMode,

    /// <summary>
    /// Apply the loaded track's stored auto cues to a deck now, without reloading the track (Slot = A/B).
    /// The DJ-facing "AUTO-CUE" action: after the auto-cue analysis has written suggested cues to the
    /// hot-cue store, this tells the deck engine to re-read its hot-cue bank from the store so the pads
    /// light up immediately (doc 11/16). The handler re-emits per-index <see cref="DeckHotCue"/> feedback
    /// so every pad reflects the refreshed bank. A no-op when no realtime engine / loaded track is present.
    /// </summary>
    DeckApplyAutoCues,

    /// <summary>
    /// Set a deck's GRID tempo — the analyzed base BPM the beat grid is drawn from and Sync references
    /// (Slot = A/B, Value = BPM). Used to hand-correct an inaccurate auto-detected tempo so the grid lands
    /// on the kicks (a "grid edit"). Distinct from <see cref="DeckBpm"/>, which changes
    /// the AUDIBLE pitch: a grid edit must NOT alter the playing pitch. Pairs with <see cref="DeckSetFirstBeat"/>
    /// (slide the grid to the playhead) and is persisted to the catalog as a manual beat grid.
    /// </summary>
    DeckSetGridBpm,

    /// <summary>
    /// Momentary pitch-bend for manual beat-matching (the NUDGE buttons / platter push): Value is the signed
    /// rate offset as a fraction (e.g. +0.03 = +3% faster, -0.03 = slower), 0 = release/restore. Temporarily
    /// scales the deck's playback rate WITHOUT moving the pitch fader or the nominal BPM, sliding the deck's
    /// phase so beats drift into alignment — never a position seek (which would skip). Restored to the
    /// deck's normal rate on release. A synced deck ignores it (Sync owns the rate).
    /// </summary>
    DeckPitchBend,

    /// <summary>
    /// Toggle the master "smart limiter" between SAFE (fixed-release brick wall) and SMART (program-
    /// dependent release that adapts to what is playing — no pumping on dense kicks, transparent on
    /// breakdowns). No value; the handler flips the current state and reports the active mode back so a
    /// button/LED reflects it (doc 11). Drives the master <c>MasterLimiter</c> via the mixer seam.
    /// </summary>
    MixerLimiterSmart,

    /// <summary>
    /// Set the smart limiter's CHARACTER knob, <see cref="PerformanceAction.Value"/> in 0..1 (absolute)
    /// or a signed delta (relative): 0 = Transparent (longer, gentler releases), 1 = Punchy (faster
    /// releases). Biases the adaptive-release range; only audible while SMART is on. Clamped on apply.
    /// </summary>
    MixerLimiterCharacter,

    /// <summary>
    /// Set the limiter's true-peak output ceiling, <see cref="PerformanceAction.Value"/> in dBTP
    /// (absolute, ≤ 0; e.g. −1.0) or a signed dB delta (relative). The brick-wall guarantee holds at this
    /// ceiling regardless of SAFE/SMART; clamped to a sane sub-0-dB range on apply so it can never reach
    /// full scale.
    /// </summary>
    MixerLimiterCeiling,

    /// <summary>
    /// Set a deck's DOWNBEAT (the musical "one", beat 1 of the bar): Slot = A/B, Value = offset in seconds
    /// from track start. Where <see cref="DeckSetFirstBeat"/> fixes the BEAT phase (where beats land, mod one
    /// beat), this fixes the BAR phase (which beat is the bar start) so the waveform's red bar markers sit on
    /// the one — a "set the downbeat". Display/grid-level only: it moves the bar emphasis,
    /// never a beat line or the audible pitch. Echoed as feedback so a deck UI can re-anchor its bars.
    /// </summary>
    DeckSetDownbeat,

    /// <summary>
    /// Set how visual scene-pad launches snap to the shared beat clock: <see cref="PerformanceAction.Value"/>
    /// selects the quantum — 0 = off (immediate), 1 = next beat, 2 = next bar. A pad pressed mid-phrase then
    /// drops on the next boundary, locking visuals to the audio grid (doc 08/31). Owned by the visual handler.
    /// </summary>
    VisualSetLaunchQuantize,

    /// <summary>Halve the deck's active loop length, keeping the in-point fixed (Slot = A/B); a no-op when
    /// the deck is not looping. The loop-creativity tool every DJ reaches for (doc 31 #9).</summary>
    DeckLoopHalve,

    /// <summary>Double the deck's active loop length, keeping the in-point fixed (Slot = A/B); a no-op when
    /// the deck is not looping.</summary>
    DeckLoopDouble,

    /// <summary>Clear (delete) a hot cue: Slot = A/B, Argument = the pad index. Lets a mis-placed cue be
    /// removed (shift+pad on hardware) and the removal persists (doc 31 #9). A no-op on an empty pad.</summary>
    DeckHotCueClear,

    /// <summary>Momentary EQ kill: while the button is held the band is fully cut; on release it restores
    /// to where it was. Slot = A/B, Argument = the band (Low/Mid/High). Uses the press/release seam
    /// (doc 31) — bind it with ReportRelease so the hold/restore both fire.</summary>
    MixerEqKill,

    /// <summary>Cue-play preview (CDJ press-and-hold): press plays from the deck's cue point (setting it
    /// at the current position when none is set); release returns to the cue and pauses. Slot = A/B. Uses
    /// the press/release seam (doc 31) — bind with ReportRelease. Distinct from <see cref="DeckCue"/> so a
    /// plain click (no release) keeps the back-to-cue behavior.</summary>
    DeckCuePlay,

    /// <summary>
    /// Toggle mute of one of a stem-deck's four stems (doc 32 §Phase 2b): Slot = A/B, Argument = the
    /// <see cref="Analysis.Stems.StemKind"/> name (Drums/Bass/Vocals/Other). A no-op when the deck is a
    /// normal single-file deck (stems absent / gate off). Mute is a per-transition gesture that belongs to
    /// the loaded track — it resets to all-audible on every load, unlike the persistent Gain/EQ. Feedback
    /// carries the stem name in <see cref="PerformanceAction.Argument"/> and IsActive = the stem is AUDIBLE
    /// (lit = playing), so a per-stem button reflects state; IsAvailable = the deck is a stem deck.
    /// </summary>
    DeckStemMute,

    /// <summary>
    /// Set one stem's volume on a stem deck to a continuous 0..1 level (doc 32 §2b, DJ PRO stem knobs):
    /// Slot = A/B, Argument = the <see cref="Analysis.Stems.StemKind"/> name, Value = 0..1 gain (Absolute).
    /// Distinct from <see cref="DeckStemMute"/> (a per-transition on/off) — this is the always-on stem level
    /// knob. A no-op for a single-file deck; reset to unity on load. Feedback echoes the set Value with the
    /// stem name in Argument and IsAvailable = the deck is a stem deck.
    /// </summary>
    DeckStemGain,

    /// <summary>
    /// Set whether a deck's analyzed beatgrid is trustworthy enough to PHASE-sync: Slot = A/B, Value = 1
    /// (grid confident — offer beat/phase sync) or 0 (grid uncertain — Sync tempo-matches only, no phase
    /// align). The gate decision is computed in Core from the track's grid-confidence signals
    /// (<see cref="Liveolator.Core.Analysis.Bpm.GridConfidenceCalculator"/>) and fed on load, like the
    /// downbeat. Never touches audible pitch. Echoed as feedback so a deck UI can show "grid uncertain"
    /// (SYNC-BEHAVIOR-SPEC §7). Defaults to confident/preserve until a track with signals loads.
    /// </summary>
    DeckSetPhaseSyncReady,

    /// <summary>
    /// Toggle persistent TEMPO-ONLY sync to the other deck (SYNC-BEHAVIOR-SPEC §4): beatmatch and keep
    /// following the master's tempo, but never align or correct the beat phase — the DJ rides the "one" by
    /// hand. The tempo-only alternate to <see cref="DeckSyncToggle"/> (Sync Lock); the two share one latch,
    /// so engaging either switches the deck's sync mode. Owned by the deck handler.
    /// </summary>
    DeckTempoSyncToggle,
}
