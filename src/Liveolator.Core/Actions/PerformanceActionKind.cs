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

    /// <summary>
    /// Loads a controllable generator preset (doc 28) onto a layer: Argument is the preset id, Slot is
    /// the target layer. The handler expands the preset into the generator source + its ≤5 controllable
    /// macros, which are then driven by <see cref="VisualSetMacro"/> from UI knobs / external controllers.
    /// </summary>
    VisualLoadPreset,

    // Auto-mix (doc 11) — hands-free deck-to-deck transition, beat-locked to the one shared clock.
    // Appended at the tail to preserve the serialized numeric values of existing kinds.

    /// <summary>Start an automatic deck-to-deck transition, or abort the one in flight.</summary>
    AutomixToggle,

    /// <summary>
    /// Set the auto-mix transition length: Value is the 0..1 knob position, resolved to a bar-count
    /// detent (2/4/8/16/32/64 bars) by the auto-mix engine. Applies to the NEXT transition.
    /// </summary>
    AutomixSetDuration,

    /// <summary>
    /// Select the auto-mix transition style: Argument is the <c>AutomixStyle</c> name
    /// (CrossFade / EqMix / FxMix).
    /// </summary>
    AutomixSetStyle,

    /// <summary>
    /// Appends a track to a deck's live queue without touching what is playing: Slot is the deck
    /// (A = 0, B = 1), Argument is the track path. Emitted instead of <see cref="DeckLoadTrack"/>
    /// when the target deck is playing, so a load can never cut off the floor's audio — the queued
    /// track plays when the current one ends (doc 09/11).
    /// </summary>
    PlaylistAppendTrack,
}
