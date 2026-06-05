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
    TransportPlayPause,
    TransportStop,
    TransportNextTrack,
    TransportPreviousTrack,
    TransportQueueTrack,
    TransportLoadSelectedTrack,
    TransportToggleAutoAdvance,

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
    VisualSetLayerSource,
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
    DeckSyncLockToggle,
    DeckQuantizeToggle,

    // Mixer (doc 11)
    MixerCrossfade,
    MixerChannelGain,
    MixerEqBand,
    MixerFilter,
    MixerCueToggle,

    // Auto-mix (doc 11) — hands-free assist
    AutoMixToggle,
    AutoMixSkipToNext,

    // Playlist
    PlaylistInsertTrackNext,
    PlaylistMoveTrack,
    PlaylistRemoveFutureTrack,
    PlaylistSkipOnNextBar,
}
