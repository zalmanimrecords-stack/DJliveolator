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

    // Auto-mix (doc 11) — hands-free assist

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
}
