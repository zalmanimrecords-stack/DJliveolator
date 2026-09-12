using Liveolator.Core.Actions;
using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Analysis.Stems;

namespace Liveolator.Core.Mapping;

/// <summary>
/// The single table that says, for every <see cref="PerformanceActionKind"/>, what the control is
/// called and how it is driven. MIDI learn generates its target list from this instead of hand-listing
/// a subset, so a kind added to the action vocabulary is bindable by default (doc 05/31, doc
/// core-business-logic/06). <see cref="NotBindable"/> is the explicit, justified opt-out; a test
/// asserts the two together cover every declared kind, so a new kind cannot be added without a
/// deliberate decision.
/// </summary>
public static class ActionTargetVocabulary
{
    private static readonly string[] EqBands = ["Low", "Mid", "High"];

    private static readonly string[] StemNames = Enum.GetNames<StemKind>();

    private static readonly string[] HotCuePads =
        Enumerable.Range(0, TrackCueSet.DefaultSlotCount)
            .Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

    /// <summary>Every bindable kind and how it presents itself as a learn target.</summary>
    public static IReadOnlyDictionary<PerformanceActionKind, ActionTarget> Targets { get; } =
        new Dictionary<PerformanceActionKind, ActionTarget>
        {
            // Transport / deck — Slot is the deck, so each gets a per-deck target.
            [PerformanceActionKind.TransportStop] = new("Stop", ActionInputMode.Momentary, PerDeck: true),
            [PerformanceActionKind.DeckPlayPause] = new("Play / Pause", ActionInputMode.Momentary, PerDeck: true),
            [PerformanceActionKind.DeckCue] = new("Cue", ActionInputMode.Momentary, PerDeck: true),
            // Press-and-hold preview: the enum doc says bind it with ReportRelease.
            [PerformanceActionKind.DeckCuePlay] = new("Cue play (hold)", ActionInputMode.Momentary, PerDeck: true),
            [PerformanceActionKind.DeckHotCue] = new("Hot cue pad", ActionInputMode.Momentary, PerDeck: true, HotCuePads),
            [PerformanceActionKind.DeckHotCueClear] = new("Clear hot cue pad", ActionInputMode.Momentary, PerDeck: true, HotCuePads),
            [PerformanceActionKind.DeckApplyAutoCues] = new("Apply auto cues", ActionInputMode.Momentary, PerDeck: true),
            [PerformanceActionKind.DeckSetLoop] = new("Loop", ActionInputMode.Absolute, PerDeck: true),
            [PerformanceActionKind.DeckLoopHalve] = new("Loop halve", ActionInputMode.Momentary, PerDeck: true),
            [PerformanceActionKind.DeckLoopDouble] = new("Loop double", ActionInputMode.Momentary, PerDeck: true),
            [PerformanceActionKind.DeckSeek] = new("Seek / track position", ActionInputMode.Absolute, PerDeck: true),
            [PerformanceActionKind.DeckJog] = new("Jog / track position", ActionInputMode.Relative, PerDeck: true),
            [PerformanceActionKind.DeckPitch] = new("Pitch fader", ActionInputMode.Absolute, PerDeck: true),
            // Signed BPM delta from a nudge button / encoder — the bindable half of the BPM pair.
            [PerformanceActionKind.DeckBpmNudge] = new("BPM nudge", ActionInputMode.Relative, PerDeck: true),
            // Signed rate offset as a fraction; 0 restores. A relative control expresses exactly that.
            [PerformanceActionKind.DeckPitchBend] = new("Pitch bend", ActionInputMode.Relative, PerDeck: true),
            [PerformanceActionKind.DeckSyncOnce] = new("Sync Once", ActionInputMode.Momentary, PerDeck: true),
            [PerformanceActionKind.DeckSyncToggle] = new("Sync", ActionInputMode.Toggle, PerDeck: true),
            [PerformanceActionKind.DeckTempoSyncToggle] = new("Sync (tempo only)", ActionInputMode.Toggle, PerDeck: true),
            [PerformanceActionKind.DeckQuantizeToggle] = new("Quantize", ActionInputMode.Toggle, PerDeck: true),
            [PerformanceActionKind.DeckKeyLockToggle] = new("Key lock", ActionInputMode.Toggle, PerDeck: true),
            [PerformanceActionKind.DeckStemMute] = new("Stem mute", ActionInputMode.Toggle, PerDeck: true, StemNames),
            [PerformanceActionKind.DeckStemGain] = new("Stem level", ActionInputMode.Absolute, PerDeck: true, StemNames),

            // Mixer — per-channel controls carry the deck in Slot; bus-wide ones do not.
            [PerformanceActionKind.MixerChannelGain] = new("Channel fader", ActionInputMode.Absolute, PerDeck: true),
            [PerformanceActionKind.MixerEqBand] = new("EQ", ActionInputMode.Absolute, PerDeck: true, EqBands),
            // Momentary kill: the enum doc says bind it with ReportRelease so hold/restore both fire.
            [PerformanceActionKind.MixerEqKill] = new("EQ kill (hold)", ActionInputMode.Momentary, PerDeck: true, EqBands),
            [PerformanceActionKind.MixerFilter] = new("Filter", ActionInputMode.Absolute, PerDeck: true),
            [PerformanceActionKind.MixerCueToggle] = new("Headphone cue", ActionInputMode.Toggle, PerDeck: true),
            [PerformanceActionKind.MixerCrossfade] = new("Mixer: Crossfader", ActionInputMode.Absolute),
            [PerformanceActionKind.MixerCueLevel] = new("Mixer: Headphone level", ActionInputMode.Absolute),
            [PerformanceActionKind.MixerCueMix] = new("Mixer: Headphone cue/master mix", ActionInputMode.Absolute),
            [PerformanceActionKind.MixerEqCutMode] = new("Mixer: EQ cut mode (EQ / DEEP / KILL)", ActionInputMode.Momentary),
            [PerformanceActionKind.MixerLimiterSmart] = new("Mixer: Limiter SAFE / SMART", ActionInputMode.Toggle),
            [PerformanceActionKind.MixerLimiterCharacter] = new("Mixer: Limiter character", ActionInputMode.Absolute),
            // dBTP, but the handler clamps to a sane sub-0 range, so a fader/encoder is still usable.
            [PerformanceActionKind.MixerLimiterCeiling] = new("Mixer: Limiter ceiling", ActionInputMode.Absolute),
            [PerformanceActionKind.MasterRecordToggle] = new("Master: Record mix", ActionInputMode.Toggle),
            [PerformanceActionKind.SystemMasterVolume] = new("System: Master volume", ActionInputMode.Absolute),

            // Beat clock — one shared clock, so these are global.
            [PerformanceActionKind.BeatTapTempo] = new("Beat: Tap tempo", ActionInputMode.Momentary),
            [PerformanceActionKind.BeatLock] = new("Beat: Lock tempo", ActionInputMode.Momentary),
            [PerformanceActionKind.BeatUnlock] = new("Beat: Unlock tempo", ActionInputMode.Momentary),
            [PerformanceActionKind.BeatHalfTempo] = new("Beat: Half tempo", ActionInputMode.Momentary),
            [PerformanceActionKind.BeatDoubleTempo] = new("Beat: Double tempo", ActionInputMode.Momentary),
            [PerformanceActionKind.BeatNudgeForward] = new("Beat: Nudge forward", ActionInputMode.Momentary),
            [PerformanceActionKind.BeatNudgeBackward] = new("Beat: Nudge backward", ActionInputMode.Momentary),
            [PerformanceActionKind.BeatResetGrid] = new("Beat: Reset grid", ActionInputMode.Momentary),
            [PerformanceActionKind.BeatSetDownbeat] = new("Beat: Set downbeat", ActionInputMode.Momentary),

            // Visuals — Slot addresses a scene pad / bank / layer, so a bound control drives slot 0 unless
            // the profile overrides it; the visual grid UI is what addresses the rest.
            [PerformanceActionKind.VisualLoadScene] = new("Visuals: Load scene", ActionInputMode.Momentary),
            [PerformanceActionKind.VisualSelectBank] = new("Visuals: Select bank", ActionInputMode.Momentary),
            [PerformanceActionKind.VisualToggleLayer] = new("Visuals: Toggle layer", ActionInputMode.Toggle),
            [PerformanceActionKind.VisualSetLayerOpacity] = new("Visuals: Layer opacity", ActionInputMode.Absolute),
            [PerformanceActionKind.VisualBlackout] = new("Visuals: Blackout", ActionInputMode.Toggle),
            [PerformanceActionKind.VisualToggleStrobe] = new("Visuals: Strobe", ActionInputMode.Toggle),
            [PerformanceActionKind.VisualTransitionNow] = new("Visuals: Transition now", ActionInputMode.Momentary),
            [PerformanceActionKind.VisualTransitionNextBeat] = new("Visuals: Transition on next beat", ActionInputMode.Momentary),
            [PerformanceActionKind.VisualTransitionNextBar] = new("Visuals: Transition on next bar", ActionInputMode.Momentary),
            // The handler reads 0 / 1 / 2 but explicitly tolerates a 0..1 knob source.
            [PerformanceActionKind.VisualSetLaunchQuantize] = new("Visuals: Launch quantize", ActionInputMode.Absolute),

            // Playlist — the live queue per deck.
            [PerformanceActionKind.PlaylistSkipOnNextBar] = new("Skip on next bar", ActionInputMode.Momentary, PerDeck: true),

        };

    /// <summary>
    /// Kinds deliberately kept OUT of the learn-target list, because a <see cref="ControllerBinding"/>
    /// cannot carry what the handler requires. Each entry is a structural limitation of the binding
    /// seam, not a gap in this table:
    /// <list type="bullet">
    /// <item><description><c>DeckLoadTrack</c>, <c>PlaylistAppendTrack</c>, <c>PlaylistInsertTrackNext</c>
    /// — need a track PATH in Argument; a knob or pad has no way to name a file (the library browser
    /// emits these).</description></item>
    /// <item><description><c>PlaylistMoveTrack</c>, <c>PlaylistRemoveFutureTrack</c> — need a live-queue
    /// entry Guid in Argument, which only the queue UI knows.</description></item>
    /// <item><description><c>VisualLaunchClip</c>, <c>VisualLoadPreset</c>, <c>VisualSetLayerSource</c>
    /// — need a clip id / preset id / encoded source ref in Argument, supplied by the visual
    /// library.</description></item>
    /// <item><description><c>VisualSetMacro</c> — needs a macro name; it IS reachable, generated once per
    /// controllable parameter of every registered generator preset, so it is excluded here rather than
    /// offered as a nameless target.</description></item>
    /// <item><description><c>AudioFxLoad</c> — needs a plugin UID in Argument.</description></item>
    /// <item><description><c>AudioFxUnload</c>, <c>AudioFxMove</c>, <c>AudioFxToggleBypass</c>,
    /// <c>AudioFxSetParameter</c>, <c>AudioFxLoadPreset</c> — need
    /// <c>PerformanceAction.Target</c> (which loaded effect instance in the rack).
    /// <see cref="ControllerBinding"/> has NO Target field, so a bound control would throw inside the
    /// handler. Binding rack effects needs a per-instance mapping seam, not a generated list.</description></item>
    /// <item><description><c>DeckSetPhaseSyncReady</c> — a grid-confidence gate computed in Core from the
    /// track's analysis and fed on load; it is not a performer control.</description></item>
    /// <item><description><c>DeckBpm</c>, <c>DeckSetGridBpm</c>, <c>DeckSetFirstBeat</c>,
    /// <c>DeckSetDownbeat</c> — their Value is a physical quantity (a BPM, a position in seconds) that
    /// the handler passes straight to the engine, while a binding can only produce a 0..1 fraction or a
    /// small signed step. A learned control would set a deck to half a BPM or anchor its downbeat three
    /// milliseconds in. <c>DeckSetGridBpm</c> is the dangerous one: it persists a MANUAL beat grid, and a
    /// manual grid is protected from re-analysis, so one stray fader move would permanently pin a track
    /// to a nonsense tempo. Tempo by ear has a kind that IS shaped for a control —
    /// <c>DeckBpmNudge</c>, whose Value is a signed BPM delta — and that one is offered.</description></item>
    /// </list>
    /// </summary>
    public static IReadOnlySet<PerformanceActionKind> NotBindable { get; } =
        new HashSet<PerformanceActionKind>
        {
            PerformanceActionKind.DeckLoadTrack,
            PerformanceActionKind.PlaylistAppendTrack,
            PerformanceActionKind.PlaylistInsertTrackNext,
            PerformanceActionKind.PlaylistMoveTrack,
            PerformanceActionKind.PlaylistRemoveFutureTrack,
            PerformanceActionKind.VisualLaunchClip,
            PerformanceActionKind.VisualLoadPreset,
            PerformanceActionKind.VisualSetLayerSource,
            PerformanceActionKind.VisualSetMacro,
            PerformanceActionKind.AudioFxLoad,
            PerformanceActionKind.AudioFxUnload,
            PerformanceActionKind.AudioFxMove,
            PerformanceActionKind.AudioFxToggleBypass,
            PerformanceActionKind.AudioFxSetParameter,
            PerformanceActionKind.AudioFxLoadPreset,
            PerformanceActionKind.DeckSetPhaseSyncReady,
            PerformanceActionKind.DeckBpm,
            PerformanceActionKind.DeckSetGridBpm,
            PerformanceActionKind.DeckSetFirstBeat,
            PerformanceActionKind.DeckSetDownbeat,
        };
}
