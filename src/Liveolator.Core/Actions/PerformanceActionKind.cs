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
}
