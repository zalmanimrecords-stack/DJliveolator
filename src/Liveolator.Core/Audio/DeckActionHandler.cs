using Liveolator.Core.Actions;
using Liveolator.Core.Analysis.Stems;
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
        PerformanceActionKind.DeckPitchBend,
        PerformanceActionKind.DeckCue,
        PerformanceActionKind.DeckCuePlay,
        PerformanceActionKind.DeckSyncOnce,
        PerformanceActionKind.DeckSyncToggle,
        PerformanceActionKind.DeckQuantizeToggle,
        PerformanceActionKind.DeckKeyLockToggle,
        PerformanceActionKind.DeckHotCue,
        PerformanceActionKind.DeckHotCueClear,
        PerformanceActionKind.DeckApplyAutoCues,
        PerformanceActionKind.DeckSetLoop,
        PerformanceActionKind.DeckLoopHalve,
        PerformanceActionKind.DeckLoopDouble,
        PerformanceActionKind.DeckSetFirstBeat,
        PerformanceActionKind.DeckSetDownbeat,
        PerformanceActionKind.DeckSetGridBpm,
        PerformanceActionKind.DeckStemMute,
    };

    // A monotonic seconds source used to time jog ticks (for velocity) and to release a stale bend. A
    // process-wide Stopwatch keeps it cheap and platform-agnostic; tests inject a fake clock instead.
    private static readonly System.Diagnostics.Stopwatch MonotonicClock = System.Diagnostics.Stopwatch.StartNew();

    private readonly IMultiDeckPlaybackEngine _engine;
    private readonly JogWheelSettings _jogSettings;
    private readonly Func<double> _nowSeconds;
    // Per-deck jog→pitch-bend state (playing jog = beat-match nudge). One tracker per slot.
    private readonly JogBendTracker[] _jogBend;
    private readonly ActionFeedbackState[] _loadedTracks;
    // The per-deck downbeat (bar-1) anchor in seconds, mirrored here for feedback (the engine seam has a
    // setter but no getter) so a deck UI can re-anchor its bar markers and a session restore can re-apply a
    // manually-set "one". The anchor itself is forwarded to the engine for bar-level phase alignment.
    private readonly double[] _downbeats;

    /// <summary>Wraps a single-deck engine (slot 0 only) — the existing composition.</summary>
    public DeckActionHandler(IAudioPlaybackEngine engine)
        : this(new SingleDeckEngineAdapter(engine ?? throw new ArgumentNullException(nameof(engine))), null)
    {
    }

    /// <summary>Drives a two-deck engine directly, addressing decks by action slot.</summary>
    /// <param name="nowSeconds">Monotonic seconds source for jog timing; defaults to a process Stopwatch.
    /// Injected in tests so bend/release timing is deterministic.</param>
    public DeckActionHandler(
        IMultiDeckPlaybackEngine engine,
        JogWheelSettings? jogSettings = null,
        Func<double>? nowSeconds = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _jogSettings = (jogSettings ?? JogWheelSettings.Default).Normalized();
        _nowSeconds = nowSeconds ?? (() => MonotonicClock.Elapsed.TotalSeconds);
        _jogBend = new JogBendTracker[engine.DeckCount];
        for (int i = 0; i < _jogBend.Length; i++)
            _jogBend[i] = new JogBendTracker(_jogSettings);
        _loadedTracks = Enumerable.Repeat(ActionFeedbackState.Unavailable, engine.DeckCount).ToArray();
        _downbeats = new double[engine.DeckCount];
        // The continuous correction loop moves a deck's lock state (Active->Locked->Drifting) on the sync
        // pump thread with no action dispatched, so re-emit DeckSyncToggle feedback on every transition —
        // that is how the SYNC LED / on-screen indicator follows the live state (push, not a UI poll). The
        // engine raises this off its gate; feedback subscribers already marshal to their own thread. The
        // handler and engine are composition-root singletons with the same lifetime, so this never leaks.
        _engine.SyncStateChanged += OnEngineSyncStateChanged;
    }

    /// <inheritdoc />
    public override IReadOnlySet<PerformanceActionKind> HandledKinds => Kinds;

    /// <summary>
    /// Releases any playing-jog pitch-bend whose ticks have stopped, restoring the deck's normal rate.
    /// An endless jog encoder sends no "release" event, so the composition root calls this periodically
    /// (~30 ms). Cheap: a per-deck staleness check that touches the engine only on an actual release.
    /// </summary>
    public void PumpJogRelease()
    {
        double now = _nowSeconds();
        for (int slot = 0; slot < _jogBend.Length; slot++)
            if (_jogBend[slot].TryReleaseStale(now))
                _engine.PitchBend(slot, 0.0);
    }

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
                LoadTrackOrSurfaceFailure(slot, action);
                break;
            case PerformanceActionKind.DeckSetLoop:
                SetLoop(slot, action);
                break;
            case PerformanceActionKind.DeckLoopHalve:
                _engine.HalveLoop(slot);
                RaiseFeedback(PerformanceActionKind.DeckSetLoop, slot, LoopFeedback(slot));
                break;
            case PerformanceActionKind.DeckLoopDouble:
                _engine.DoubleLoop(slot);
                RaiseFeedback(PerformanceActionKind.DeckSetLoop, slot, LoopFeedback(slot));
                break;
            case PerformanceActionKind.DeckSetFirstBeat:
                // The analyzed first-beat (downbeat) anchor in seconds — feeds phase-match the same way
                // SetDeckBaseBpm feeds tempo-match. Emitted right after DeckLoadTrack by the source that
                // holds the full BpmResult (doc 11 / doc 22 A1). Echoed as feedback so the deck UI can
                // anchor its beat/bar grid on the same downbeat the engine syncs to (grid sits on the kick).
                _engine.SetDeckFirstBeat(slot, action.Value);
                RaiseFeedback(PerformanceActionKind.DeckSetFirstBeat, slot, ValueFeedback(action.Value));
                break;
            case PerformanceActionKind.DeckSetDownbeat:
                // The bar-1 ("one") anchor in seconds. Forwarded to the engine so Quantize/SYNC phase-match
                // can snap onto the leader's DOWNBEAT (bar-level) when both decks know theirs; also recorded
                // and echoed so the deck UI re-anchors its red bar markers on the one (and a session restore
                // can re-apply a manual edit). Never touches the audible pitch/rate.
                _engine.SetDeckDownbeat(slot, action.Value);
                _downbeats[slot] = action.Value;
                RaiseFeedback(PerformanceActionKind.DeckSetDownbeat, slot, ValueFeedback(action.Value));
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
                if (_engine.IsPlaying(slot))
                {
                    // PLAYING: a jog is a temporary pitch-bend (beat-match nudge), NOT a position seek. A
                    // seek flushes the deck buffer (audible click, and historically leaked across decks via
                    // the shared mixer); a rate bend slides the phase glitch-free. The bend is held until
                    // the ticks stop (PumpJogRelease) — an endless encoder sends no release. No seek
                    // feedback: the playhead isn't jumped, so nothing on the transport moves.
                    _engine.PitchBend(slot, _jogBend[slot].OnJog(action.Value, _nowSeconds()));
                }
                else
                {
                    // PAUSED: scrub the platter to find a cue, like a record under the hand.
                    _engine.Jog(slot, action.Value * _jogSettings.PausedSecondsPerRevolution);
                    RaiseFeedback(PerformanceActionKind.DeckSeek, slot, ValueFeedback(_engine.Position(slot)));
                }
                break;
            case PerformanceActionKind.DeckPitch:
                _engine.SetPitch(slot, action.Value, action.InputMode == ActionInputMode.Relative);
                RaiseFeedback(PerformanceActionKind.DeckPitch, slot, ValueFeedback(_engine.PitchPosition(slot)));
                RaiseBpmFeedback(slot);
                RaiseSyncedFollowerBpm(slot);
                break;
            case PerformanceActionKind.DeckBpm:
                _engine.SetDeckBpm(slot, action.Value);
                RaiseFeedback(PerformanceActionKind.DeckPitch, slot, ValueFeedback(_engine.PitchPosition(slot)));
                RaiseBpmFeedback(slot);
                RaiseSyncedFollowerBpm(slot);
                break;
            case PerformanceActionKind.DeckSetGridBpm:
                // Re-tempo the GRID/sync reference (a hand-corrected analyzed BPM), NOT the audible rate:
                // set base BPM only and re-emit BPM feedback so the deck rebuilds its beat grid. The pitch
                // position is left untouched, so editing the grid never changes what the floor hears.
                _engine.SetDeckBaseBpm(slot, action.Value);
                RaiseBpmFeedback(slot);
                RaiseSyncedFollowerBpm(slot);
                break;
            case PerformanceActionKind.DeckPitchBend:
                // Momentary rate bend for manual beat-matching (Value = signed fraction, 0 = release). No
                // feedback: it doesn't move the pitch fader or nominal BPM, so no indicator changes.
                _engine.PitchBend(slot, action.Value);
                break;
            case PerformanceActionKind.DeckBpmNudge:
                // Relative delta in BPM (+0.1 / -0.1 from nudge buttons). The engine's SetDeckBpm
                // saturates at the ±8% pitch rail, so repeated nudges past the rail simply hold there
                // (no explicit clamp needed here).
                _engine.SetDeckBpm(slot, _engine.DeckBpm(slot) + action.Value);
                RaiseFeedback(PerformanceActionKind.DeckPitch, slot, ValueFeedback(_engine.PitchPosition(slot)));
                RaiseBpmFeedback(slot);
                RaiseSyncedFollowerBpm(slot);
                break;
            case PerformanceActionKind.DeckCuePlay:
                _engine.CuePlay(slot, action.IsPressed);
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
                // One-shot SYNC beatmatches with the musical key preserved (engine engages key-lock), so
                // echo the key-lock state too — the on-screen KEY LOCK button lights and the deck's audible
                // state stays truthful after the sync.
                RaiseFeedback(PerformanceActionKind.DeckKeyLockToggle, slot, ActiveFeedback(_engine.IsKeyLockEnabled(slot)));
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
            case PerformanceActionKind.DeckHotCueClear:
                TriggerHotCueClear(slot, action);
                break;
            case PerformanceActionKind.DeckApplyAutoCues:
                ApplyAutoCues(slot);
                break;
            case PerformanceActionKind.DeckStemMute:
                ToggleStemMute(slot, action);
                break;
            default:
                break; // dispatcher guarantees only handled kinds reach here
        }
    }

    // Loads the track and reports the outcome as DeckLoadTrack feedback. On success the feedback carries
    // the path (Argument) + analyzed BPM (Value, 0 = unknown) so the deck UI builds its waveform/grid. On
    // FAILURE — the engine could not open the file (missing/offline drive) or the native audio engine
    // could not create the deck stream — it raises a load-failed feedback (IsAvailable:false) so the deck
    // UI shows a clear failure instead of a silently empty deck with dead transport buttons (global
    // standards #16/#26), then rethrows so the dispatcher logs the full cause.
    private void LoadTrackOrSurfaceFailure(int slot, PerformanceAction action)
    {
        try
        {
            LoadTrack(slot, action);
        }
        catch (Exception)
        {
            RaiseFeedback(
                PerformanceActionKind.DeckLoadTrack, slot,
                _loadedTracks[slot] = new ActionFeedbackState(
                    IsActive: false, IsAvailable: false, Value: 0, Argument: action.Argument));
            throw;
        }

        RaiseFeedback(
            PerformanceActionKind.DeckLoadTrack, slot,
            _loadedTracks[slot] = new ActionFeedbackState(
                IsActive: true, IsAvailable: true, Value: action.Value, Argument: action.Argument));
    }

    private void LoadTrack(int slot, PerformanceAction action)
    {
        _engine.Load(slot, action.Argument!);
        // Value carries the track's analyzed BPM (0 = unknown), feeding the deck's Sync reference tempo so
        // beatmatching can match against it (doc 11) — kept on the action seam, no new kind.
        _engine.SetDeckBaseBpm(slot, action.Value);
        RaiseBpmFeedback(slot);
        // The downbeat ("one") belongs to the TRACK, like the hot cues: clear the previous track's anchor
        // on load and echo the reset, so a stale bar-1 can never be read as this load's (the deck UI and
        // session persistence both ignore the 0 echo by design — it never erases a saved anchor).
        _downbeats[slot] = 0;
        RaiseFeedback(PerformanceActionKind.DeckSetDownbeat, slot, ValueFeedback(0));
        // The first-beat (downbeat) anchor — BpmResult.FirstBeatSeconds — feeds phase-match the same way
        // base BPM feeds tempo-match. The single-Value load action carries the BPM only, so the anchor is
        // supplied separately via SetDeckFirstBeat by the composition root that holds the full BpmResult
        // (the engine defaults to a 0 anchor, leaving phase-match a no-op, until one is set).
        // Stems: the engine opens fresh decoders at unity (all audible) and mute is per-track, so relight
        // every stem button to audible + refresh its availability (a stem deck vs a plain single-file deck).
        RaiseStemFeedback(slot);
    }

    // Re-emit the mute/availability state of all four stems for a deck (doc 32 §2b) — used on load so the
    // per-stem buttons enable only for a stem deck and reset to audible, mirroring how the hot-cue pads
    // relight on load. A no-op-looking push on a single-file deck (IsAvailable:false) disables the buttons.
    private void RaiseStemFeedback(int slot)
    {
        foreach (StemKind kind in StemSet.RequiredStems)
            RaiseFeedback(PerformanceActionKind.DeckStemMute, slot, StemFeedback(slot, kind));
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

    // Flip one stem's mute on the deck and echo its state (doc 32 §2b). The engine no-ops for a single-file
    // deck, so a press on a disabled control is harmless; the echoed IsAvailable tells the button it's dead.
    private void ToggleStemMute(int slot, PerformanceAction action)
    {
        StemKind kind = ParseStemKind(action.Argument);
        _engine.SetStemMuted(slot, kind, !_engine.IsStemMuted(slot, kind));
        RaiseFeedback(PerformanceActionKind.DeckStemMute, slot, StemFeedback(slot, kind));
    }

    // DeckStemMute feedback: IsActive = the stem is AUDIBLE (lit = playing, per the design line), Argument =
    // the stem name so one push updates the matching button, IsAvailable = the deck is actually a stem deck.
    private ActionFeedbackState StemFeedback(int slot, StemKind kind)
        => new(
            IsActive: !_engine.IsStemMuted(slot, kind),
            IsAvailable: _engine.IsStemDeck(slot),
            Value: 0,
            Argument: kind.ToString());

    private static StemKind ParseStemKind(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument) || !Enum.TryParse(argument, ignoreCase: true, out StemKind kind)
            || !Enum.IsDefined(kind))
            throw new ArgumentException(
                "DeckStemMute requires Argument set to a stem name (Drums/Bass/Vocals/Other).", nameof(argument));
        return kind;
    }

    private void ToggleSync(int slot)
    {
        _engine.SetSyncLock(slot, !_engine.IsSyncLocked(slot));
        RaiseFeedback(PerformanceActionKind.DeckSyncToggle, slot, SyncFeedback(slot));
        RaiseFeedback(PerformanceActionKind.DeckPitch, slot, ValueFeedback(_engine.PitchPosition(slot)));
        RaiseBpmFeedback(slot);
        RaiseFeedback(PerformanceActionKind.DeckSeek, slot, ValueFeedback(_engine.Position(slot)));
    }

    // Re-emit sync feedback when the engine reports a lock-state transition (Active/Locked/Drifting/
    // OutOfRange) — built from the pushed state, not a re-query, so it never re-enters the engine lock.
    private void OnEngineSyncStateChanged(int slot, SyncLockState state)
        => RaiseFeedback(PerformanceActionKind.DeckSyncToggle, slot, SyncFeedback(slot, state));

    private ActionFeedbackState SyncFeedback(int slot) => SyncFeedback(slot, _engine.SyncState(slot));

    private static ActionFeedbackState SyncFeedback(int slot, SyncLockState state)
        => new(
            IsActive: state != SyncLockState.Off,
            IsAvailable: true,
            Value: (double)state,
            Argument: state.ToString());

    private void TriggerHotCue(int slot, PerformanceAction action)
    {
        // The deck is addressed by Slot, so the hot-cue index rides in Argument (the action record has no
        // second index field). Set-or-jump is decided by the engine; here we only validate addressing.
        if (!int.TryParse(action.Argument, out int cueIndex))
            throw new ArgumentException("DeckHotCue requires Argument set to the hot-cue index.", nameof(action));
        if (cueIndex < 0 || cueIndex >= _engine.HotCueCount)
            throw new ArgumentOutOfRangeException(nameof(action), cueIndex, "Hot-cue index is out of range.");

        _engine.HotCue(slot, cueIndex);
        RaiseFeedback(PerformanceActionKind.DeckHotCue, slot, HotCueFeedbackState(slot, cueIndex));
    }

    private void TriggerHotCueClear(int slot, PerformanceAction action)
    {
        if (!int.TryParse(action.Argument, out int cueIndex))
            throw new ArgumentException("DeckHotCueClear requires Argument set to the hot-cue index.", nameof(action));
        if (cueIndex < 0 || cueIndex >= _engine.HotCueCount)
            throw new ArgumentOutOfRangeException(nameof(action), cueIndex, "Hot-cue index is out of range.");

        _engine.ClearHotCue(slot, cueIndex);
        RaiseFeedback(PerformanceActionKind.DeckHotCue, slot, HotCueFeedbackState(slot, cueIndex));
    }

    // DeckHotCue feedback carries the cue index AND its display metadata (label/color/auto) encoded in the
    // Argument, so a deck pad can show the cue's name/color, not just a lit number. IsActive is the lit state.
    private ActionFeedbackState HotCueFeedbackState(int slot, int cueIndex)
    {
        HotCueInfo info = _engine.GetHotCueInfo(slot, cueIndex);
        return new ActionFeedbackState(
            IsActive: info.IsSet,
            IsAvailable: true,
            Value: 0,
            Argument: HotCueFeedback.Encode(cueIndex, info));
    }

    private void ApplyAutoCues(int slot)
    {
        // The auto-cue analysis has already written the suggested cues to the store; re-read the deck's
        // bank from it, then relight every pad so the UI reflects the refreshed bank. The hot-cue index
        // rides in Argument for DeckHotCue feedback, mirroring how a pad press reports a single slot.
        _engine.ReloadHotCues(slot);
        for (int cueIndex = 0; cueIndex < _engine.HotCueCount; cueIndex++)
            RaiseFeedback(PerformanceActionKind.DeckHotCue, slot, HotCueFeedbackState(slot, cueIndex));
    }

    private static ActionFeedbackState ValueFeedback(double value)
        => new(IsActive: false, IsAvailable: true, Value: value);

    private static ActionFeedbackState ActiveFeedback(bool active)
        => new(IsActive: active, IsAvailable: true, Value: 0);

    private void RaiseBpmFeedback(int slot)
        => RaiseFeedback(PerformanceActionKind.DeckBpm, slot, BpmFeedback(slot));

    // A tempo change on one deck re-tempos every deck sync-locked to it (the engine's
    // ReapplySyncedFollowers). The engine recomputes a synced follower's audible BPM live, but nothing
    // re-emits its DeckBpm feedback — so the follower's on-screen counter freezes at its engage value
    // while its audio tracks the leader (the counter then lies about what's playing). Mirror the engine's
    // follower pull on the feedback side: after the moved slot, re-raise BPM for every OTHER synced deck.
    private void RaiseSyncedFollowerBpm(int movedSlot)
    {
        for (int slot = 0; slot < _engine.DeckCount; slot++)
            if (slot != movedSlot && _engine.IsSyncLocked(slot))
                RaiseBpmFeedback(slot);
    }

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
            PerformanceActionKind.DeckSetDownbeat => ValueFeedback(_downbeats[slot]),
            // No stem is addressable through the (kind, slot) pull, so report only whether the deck is a
            // stem deck; the per-stem active state is delivered by push (RaiseStemFeedback on load + toggle).
            PerformanceActionKind.DeckStemMute
                => new ActionFeedbackState(IsActive: false, IsAvailable: _engine.IsStemDeck(slot), Value: 0),
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
