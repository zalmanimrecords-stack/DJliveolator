using Liveolator.Core.Actions;
using Liveolator.Core.Audio.Sync;
using Liveolator.Core.Settings;

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
        PerformanceActionKind.DeckJog,
        PerformanceActionKind.DeckPitch,
        PerformanceActionKind.DeckBpm,
        PerformanceActionKind.DeckBpmNudge,
        PerformanceActionKind.DeckCue,
        PerformanceActionKind.DeckSyncOnce,
        PerformanceActionKind.DeckSyncToggle,
        PerformanceActionKind.DeckQuantizeToggle,
        PerformanceActionKind.DeckKeyLockToggle,
        PerformanceActionKind.DeckHotCue,
        PerformanceActionKind.DeckSetLoop,
        PerformanceActionKind.DeckSetFirstBeat,
    };

    private readonly IMultiDeckPlaybackEngine _engine;
    private readonly JogWheelSettings _jogSettings;
    private readonly ActionFeedbackState[] _loadedTracks;

    /// <summary>Wraps a single-deck engine (slot 0 only) — the existing composition.</summary>
    public DeckActionHandler(IAudioPlaybackEngine engine)
        : this(new SingleDeckEngineAdapter(engine ?? throw new ArgumentNullException(nameof(engine))), null)
    {
    }

    /// <summary>Drives a two-deck engine directly, addressing decks by action slot.</summary>
    public DeckActionHandler(IMultiDeckPlaybackEngine engine, JogWheelSettings? jogSettings = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _jogSettings = (jogSettings ?? JogWheelSettings.Default).Normalized();
        _loadedTracks = Enumerable.Repeat(ActionFeedbackState.Unavailable, engine.DeckCount).ToArray();
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
                // load-time signal back to subscribers, and it now carries the path via Argument and the
                // analyzed BPM via Value (0 = unknown) so the deck can derive a beat-grid overlay.
                RaiseFeedback(
                    PerformanceActionKind.DeckLoadTrack, slot,
                    _loadedTracks[slot] = new ActionFeedbackState(
                        IsActive: true, IsAvailable: true, Value: action.Value, Argument: action.Argument));
                break;
            case PerformanceActionKind.DeckSetLoop:
                SetLoop(slot, action);
                break;
            case PerformanceActionKind.DeckSetFirstBeat:
                // The analyzed first-beat (downbeat) anchor in seconds — feeds phase-match the same way
                // SetDeckBaseBpm feeds tempo-match. Emitted right after DeckLoadTrack by the source that
                // holds the full BpmResult (doc 11 / doc 22 A1). Echoed as feedback so the deck UI can
                // anchor its beat/bar grid on the same downbeat the engine syncs to (grid sits on the kick).
                _engine.SetDeckFirstBeat(slot, action.Value);
                RaiseFeedback(PerformanceActionKind.DeckSetFirstBeat, slot, ValueFeedback(action.Value));
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
            case PerformanceActionKind.DeckJog:
                double secondsPerRevolution = _engine.IsPlaying(slot)
                    ? _jogSettings.PlayingSecondsPerRevolution
                    : _jogSettings.PausedSecondsPerRevolution;
                _engine.Jog(slot, action.Value * secondsPerRevolution);
                RaiseFeedback(PerformanceActionKind.DeckSeek, slot, ValueFeedback(_engine.Position(slot)));
                break;
            case PerformanceActionKind.DeckPitch:
                _engine.SetPitch(slot, action.Value, action.InputMode == ActionInputMode.Relative);
                RaiseFeedback(PerformanceActionKind.DeckPitch, slot, ValueFeedback(_engine.PitchPosition(slot)));
                RaiseBpmFeedback(slot);
                break;
            case PerformanceActionKind.DeckBpm:
                _engine.SetDeckBpm(slot, action.Value);
                RaiseFeedback(PerformanceActionKind.DeckPitch, slot, ValueFeedback(_engine.PitchPosition(slot)));
                RaiseBpmFeedback(slot);
                break;
            case PerformanceActionKind.DeckBpmNudge:
                // Relative delta in BPM (+0.1 / -0.1 from nudge buttons). The engine's SetDeckBpm
                // saturates at the ±8% pitch rail, so repeated nudges past the rail simply hold there
                // (no explicit clamp needed here).
                _engine.SetDeckBpm(slot, _engine.DeckBpm(slot) + action.Value);
                RaiseFeedback(PerformanceActionKind.DeckPitch, slot, ValueFeedback(_engine.PitchPosition(slot)));
                RaiseBpmFeedback(slot);
                break;
            case PerformanceActionKind.DeckCue:
                _engine.Cue(slot);
                RaiseFeedback(PerformanceActionKind.DeckSeek, slot, ValueFeedback(_engine.Position(slot)));
                break;
            case PerformanceActionKind.DeckSyncOnce:
                _engine.SyncOnce(slot);
                RaiseFeedback(PerformanceActionKind.DeckPitch, slot, ValueFeedback(_engine.PitchPosition(slot)));
                RaiseBpmFeedback(slot);
                RaiseFeedback(PerformanceActionKind.DeckSeek, slot, ValueFeedback(_engine.Position(slot)));
                break;
            case PerformanceActionKind.DeckSyncToggle:
                ToggleSync(slot);
                break;
            case PerformanceActionKind.DeckQuantizeToggle:
                ToggleQuantize(slot);
                break;
            case PerformanceActionKind.DeckKeyLockToggle:
                ToggleKeyLock(slot);
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
        RaiseBpmFeedback(slot);
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

    private void ToggleQuantize(int slot)
    {
        bool next = !_engine.IsQuantizeEnabled(slot);
        _engine.SetQuantize(slot, next);
        RaiseFeedback(PerformanceActionKind.DeckQuantizeToggle, slot, ActiveFeedback(next));
    }

    private void ToggleKeyLock(int slot)
    {
        bool next = !_engine.IsKeyLockEnabled(slot);
        _engine.SetKeyLock(slot, next);
        RaiseFeedback(PerformanceActionKind.DeckKeyLockToggle, slot, ActiveFeedback(next));
    }

    private void ToggleSync(int slot)
    {
        _engine.SetSyncLock(slot, !_engine.IsSyncLocked(slot));
        RaiseFeedback(PerformanceActionKind.DeckSyncToggle, slot, SyncFeedback(slot));
        RaiseFeedback(PerformanceActionKind.DeckPitch, slot, ValueFeedback(_engine.PitchPosition(slot)));
        RaiseBpmFeedback(slot);
        RaiseFeedback(PerformanceActionKind.DeckSeek, slot, ValueFeedback(_engine.Position(slot)));
    }

    private ActionFeedbackState SyncFeedback(int slot)
        => new(
            IsActive: _engine.IsSyncLocked(slot),
            IsAvailable: true,
            Value: (double)_engine.SyncState(slot),
            Argument: _engine.SyncState(slot).ToString());

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

    private void RaiseBpmFeedback(int slot)
        => RaiseFeedback(PerformanceActionKind.DeckBpm, slot, BpmFeedback(slot));

    private ActionFeedbackState BpmFeedback(int slot)
        => new(
            IsActive: false,
            IsAvailable: _engine.DeckBpm(slot) > 0.0,
            Value: _engine.DeckBpm(slot),
            Argument: FormattableString.Invariant(
                $"{_engine.MinimumDeckBpm(slot):0.###}|{_engine.MaximumDeckBpm(slot):0.###}"));

    /// <inheritdoc />
    public override ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot)
    {
        if (slot < 0 || slot >= _engine.DeckCount)
            return ActionFeedbackState.Unavailable;

        return kind switch
        {
            PerformanceActionKind.DeckPlayPause => ActiveFeedback(_engine.IsPlaying(slot)),
            PerformanceActionKind.DeckLoadTrack => _loadedTracks[slot],
            PerformanceActionKind.DeckSeek => ValueFeedback(_engine.Position(slot)),
            PerformanceActionKind.DeckPitch => ValueFeedback(_engine.PitchPosition(slot)),
            PerformanceActionKind.DeckBpm => BpmFeedback(slot),
            PerformanceActionKind.DeckSyncOnce => ActiveFeedback(false),
            PerformanceActionKind.DeckSyncToggle => SyncFeedback(slot),
            PerformanceActionKind.DeckQuantizeToggle => ActiveFeedback(_engine.IsQuantizeEnabled(slot)),
            PerformanceActionKind.DeckKeyLockToggle => ActiveFeedback(_engine.IsKeyLockEnabled(slot)),
            PerformanceActionKind.DeckSetLoop => LoopFeedback(slot),
            PerformanceActionKind.DeckSetFirstBeat => ValueFeedback(_engine.DeckFirstBeat(slot)),
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
