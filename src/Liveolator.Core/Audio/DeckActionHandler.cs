using Liveolator.Core.Actions;

namespace Liveolator.Core.Audio;

/// <summary>
/// The dispatcher handler that owns deck transport actions (doc 04/11): it translates
/// load/play-pause/stop intents into deck-engine calls, so the UI, a controller, or autopilot all
/// drive playback through the one action layer rather than touching the engine directly. Reports
/// play state back as feedback for a play LED/indicator.
/// </summary>
/// <remarks>
/// Actions are addressed per deck slot via <see cref="PerformanceAction.Slot"/> (A = 0, B = 1).
/// A single-deck engine is adapted to slot 0, so the existing single-deck composition is unchanged;
/// a two-deck engine receives the slot directly.
/// </remarks>
public sealed class DeckActionHandler : PerformanceActionHandlerBase
{
    private static readonly IReadOnlySet<PerformanceActionKind> Kinds = new HashSet<PerformanceActionKind>
    {
        PerformanceActionKind.DeckLoadTrack,
        PerformanceActionKind.DeckPlayPause,
        PerformanceActionKind.TransportStop,
        PerformanceActionKind.DeckSeek,
        PerformanceActionKind.DeckPitch,
        PerformanceActionKind.DeckCue,
        PerformanceActionKind.DeckSyncLockToggle,
        PerformanceActionKind.DeckQuantizeToggle,
        PerformanceActionKind.DeckHotCue,
        PerformanceActionKind.DeckSetLoop,
    };

    private readonly IMultiDeckPlaybackEngine _engine;

    /// <summary>Wraps a single-deck engine (slot 0 only) — the existing composition.</summary>
    public DeckActionHandler(IAudioPlaybackEngine engine)
        : this(new SingleDeckEngineAdapter(engine ?? throw new ArgumentNullException(nameof(engine))))
    {
    }

    /// <summary>Drives a two-deck engine directly, addressing decks by action slot.</summary>
    public DeckActionHandler(IMultiDeckPlaybackEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <inheritdoc />
    public override IReadOnlySet<PerformanceActionKind> HandledKinds => Kinds;

    /// <inheritdoc />
    public override void Handle(PerformanceAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        int slot = ValidateSlot(action.Slot);

        switch (action.Kind)
        {
            case PerformanceActionKind.DeckLoadTrack:
                if (string.IsNullOrWhiteSpace(action.Argument))
                    throw new ArgumentException("DeckLoadTrack requires Argument set to the track path.", nameof(action));
                LoadTrack(slot, action);
                // Report the loaded path so a deck UI (waveform/title) can react — feedback is the only
                // load-time signal back to subscribers, and it now carries the path via Argument.
                RaiseFeedback(
                    PerformanceActionKind.DeckLoadTrack, slot,
                    new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0, Argument: action.Argument));
                break;
            case PerformanceActionKind.DeckSetLoop:
                SetLoop(slot, action);
                break;
            case PerformanceActionKind.DeckPlayPause:
                _engine.PlayPause(slot);
                RaisePlayFeedback(slot);
                break;
            case PerformanceActionKind.TransportStop:
                _engine.Stop(slot);
                RaisePlayFeedback(slot);
                break;
            case PerformanceActionKind.DeckSeek:
                _engine.Seek(slot, action.Value, action.InputMode == ActionInputMode.Relative);
                RaiseFeedback(PerformanceActionKind.DeckSeek, slot, ValueFeedback(_engine.Position(slot)));
                break;
            case PerformanceActionKind.DeckPitch:
                _engine.SetPitch(slot, action.Value, action.InputMode == ActionInputMode.Relative);
                RaiseFeedback(PerformanceActionKind.DeckPitch, slot, ValueFeedback(_engine.PitchPosition(slot)));
                break;
            case PerformanceActionKind.DeckCue:
                _engine.Cue(slot);
                RaiseFeedback(PerformanceActionKind.DeckSeek, slot, ValueFeedback(_engine.Position(slot)));
                break;
            case PerformanceActionKind.DeckSyncLockToggle:
                ToggleSyncLock(slot);
                break;
            case PerformanceActionKind.DeckQuantizeToggle:
                ToggleQuantize(slot);
                break;
            case PerformanceActionKind.DeckHotCue:
                TriggerHotCue(slot, action);
                break;
            default:
                break; // dispatcher guarantees only handled kinds reach here
        }
    }

    private void LoadTrack(int slot, PerformanceAction action)
    {
        _engine.Load(slot, action.Argument!);
        // Value carries the track's analyzed BPM (0 = unknown), feeding the deck's Sync reference tempo so
        // beatmatching can match against it (doc 11) — kept on the action seam, no new kind.
        _engine.SetDeckBaseBpm(slot, action.Value);
        // The first-beat (downbeat) anchor — BpmResult.FirstBeatSeconds — feeds phase-match the same way
        // base BPM feeds tempo-match. The single-Value load action carries the BPM only, so the anchor is
        // supplied separately via SetDeckFirstBeat by the composition root that holds the full BpmResult
        // (the engine defaults to a 0 anchor, leaving phase-match a no-op, until one is set).
    }

    private void SetLoop(int slot, PerformanceAction action)
    {
        // Value is the loop length in beats: > 0 sets a beat-length loop at the current playhead, <= 0
        // clears any active loop. The engine converts beats to a time region using the deck's base BPM.
        if (action.Value > 0.0)
            _engine.SetLoop(slot, action.Value);
        else
            _engine.ClearLoop(slot);
        RaiseFeedback(PerformanceActionKind.DeckSetLoop, slot, LoopFeedback(slot));
    }

    private ActionFeedbackState LoopFeedback(int slot)
        => new(IsActive: _engine.IsLooping(slot), IsAvailable: true, Value: _engine.LoopBeats(slot));

    private void ToggleSyncLock(int slot)
    {
        bool next = !_engine.IsSyncLocked(slot);
        _engine.SetSyncLock(slot, next);
        RaiseFeedback(PerformanceActionKind.DeckSyncLockToggle, slot, ActiveFeedback(next));
    }

    private void ToggleQuantize(int slot)
    {
        bool next = !_engine.IsQuantizeEnabled(slot);
        _engine.SetQuantize(slot, next);
        RaiseFeedback(PerformanceActionKind.DeckQuantizeToggle, slot, ActiveFeedback(next));
    }

    private void TriggerHotCue(int slot, PerformanceAction action)
    {
        // The deck is addressed by Slot, so the hot-cue index rides in Argument (the action record has no
        // second index field). Set-or-jump is decided by the engine; here we only validate addressing.
        if (!int.TryParse(action.Argument, out int cueIndex))
            throw new ArgumentException("DeckHotCue requires Argument set to the hot-cue index.", nameof(action));
        if (cueIndex < 0 || cueIndex >= _engine.HotCueCount)
            throw new ArgumentOutOfRangeException(nameof(action), cueIndex, "Hot-cue index is out of range.");

        _engine.HotCue(slot, cueIndex);
        RaiseFeedback(PerformanceActionKind.DeckHotCue, slot, ActiveFeedback(_engine.IsHotCueSet(slot, cueIndex)));
    }

    private static ActionFeedbackState ValueFeedback(double value)
        => new(IsActive: false, IsAvailable: true, Value: value);

    private static ActionFeedbackState ActiveFeedback(bool active)
        => new(IsActive: active, IsAvailable: true, Value: 0);

    /// <inheritdoc />
    public override ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot)
    {
        if (slot < 0 || slot >= _engine.DeckCount)
            return ActionFeedbackState.Unavailable;

        return kind switch
        {
            PerformanceActionKind.DeckPlayPause => ActiveFeedback(_engine.IsPlaying(slot)),
            PerformanceActionKind.DeckSeek => ValueFeedback(_engine.Position(slot)),
            PerformanceActionKind.DeckPitch => ValueFeedback(_engine.PitchPosition(slot)),
            PerformanceActionKind.DeckSyncLockToggle => ActiveFeedback(_engine.IsSyncLocked(slot)),
            PerformanceActionKind.DeckQuantizeToggle => ActiveFeedback(_engine.IsQuantizeEnabled(slot)),
            PerformanceActionKind.DeckSetLoop => LoopFeedback(slot),
            _ => ActionFeedbackState.Unavailable,
        };
    }

    private void RaisePlayFeedback(int slot)
        => RaiseFeedback(PerformanceActionKind.DeckPlayPause, slot, ActiveFeedback(_engine.IsPlaying(slot)));

    private int ValidateSlot(int slot)
    {
        if (slot < 0 || slot >= _engine.DeckCount)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Deck slot is out of range for this engine.");
        return slot;
    }
}
