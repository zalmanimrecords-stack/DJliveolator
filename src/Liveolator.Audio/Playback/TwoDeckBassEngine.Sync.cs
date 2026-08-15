using Liveolator.Core.Audio.Sync;
using Microsoft.Extensions.Logging;

namespace Liveolator.Audio.Playback;

/// <summary>
/// Sync surface of <see cref="TwoDeckBassEngine"/>: the SYNC engage/release, the one-shot beatmatch, the
/// continuous phase-lock correction loop (<see cref="ISyncCorrectionDriver"/>), the master-beat readout
/// the shared clock reads, and Quantize phase-match. The pure math lives in the Core
/// <c>Liveolator.Core.Audio.Sync</c> calculators; this partial owns the per-slot lock state and routing.
/// </summary>
public sealed partial class TwoDeckBassEngine
{
    public double DeckFirstBeat(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _slots[slot].FirstBeat;
    }

    public void SetDeckFirstBeat(int slot, double firstBeatSeconds)
    {
        ValidateSlot(slot);
        lock (_gate) _slots[slot].FirstBeat = Math.Max(0.0, firstBeatSeconds);
    }

    public IReadOnlyList<double> DeckKickOnsets(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _slots[slot].KickOnsets;
    }

    public void SetDeckKickOnsets(int slot, IReadOnlyList<double> kickOnsetsSeconds)
    {
        ValidateSlot(slot);
        ArgumentNullException.ThrowIfNull(kickOnsetsSeconds);
        double[] sanitized = kickOnsetsSeconds
            .Where(v => double.IsFinite(v) && v >= 0.0)
            .Distinct()
            .OrderBy(v => v)
            .ToArray();
        lock (_gate) _slots[slot].KickOnsets = sanitized;
    }

    public void SetDeckDownbeat(int slot, double downbeatSeconds)
    {
        ValidateSlot(slot);
        lock (_gate) _slots[slot].Downbeat = Math.Max(0.0, downbeatSeconds);
    }

    public bool DeckPhaseSyncReady(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _slots[slot].PhaseSyncReady;
    }

    public void SetDeckPhaseSyncReady(int slot, bool ready)
    {
        ValidateSlot(slot);
        lock (_gate) _slots[slot].PhaseSyncReady = ready;
    }

    public SyncMode DeckSyncMode(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _slots[slot].SyncMode;
    }

    public void SetDeckSyncMode(int slot, SyncMode mode)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            DeckSlot s = _slots[slot];
            if (s.SyncMode == mode)
                return;
            s.SyncMode = mode;
            // Switching an engaged latch to BeatLock snaps the phase now so it locks immediately (the loop
            // then holds it); PhaseAlignToLeader's own gate still guards grid confidence. Switching to
            // TempoOnly drops phase-hold to tempo-tracking — report Active now (honest UI) rather than wait
            // for the next correction tick. Tempo is never touched by a mode change.
            if (!s.SyncLocked || ValidLeaderSlot(slot) < 0)
                return;
            if (mode == SyncMode.BeatLock)
                PhaseAlignToLeader(slot);
            else if (s.SyncState is SyncLockState.Locked or SyncLockState.Drifting)
                SetSyncStateLocked(slot, SyncLockState.Active);
        }
        FlushSyncTransitions();
    }

    public void SyncOnce(int slot)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            DeckSlot s = _slots[slot];
            if (s.Deck is not { } deck || s.BaseBpm <= 0.0)
            {
                // Never fail silently (global standard #16/#26): a one-shot SYNC that does nothing must say
                // why, or it reads as a dead button. The usual cause is a track loaded without its analyzed
                // BPM reaching the engine (see TryGetByPathOrName on the load path).
                _logger.LogInformation(
                    "Deck slot {Slot} one-shot sync skipped: own track/BPM unknown (BaseBpm {Bpm:F1}).",
                    slot, s.BaseBpm);
                return;
            }

            int leaderSlot = slot == 0 ? 1 : 0;
            DeckSlot leader = _slots[leaderSlot];
            if (leader.Deck is not { Playing: true } || leader.BaseBpm <= 0.0)
            {
                _logger.LogInformation(
                    "Deck slot {Slot} one-shot sync skipped: no live playing leader on deck {Leader} (BaseBpm {Bpm:F1}).",
                    slot, leaderSlot, leader.BaseBpm);
                return;
            }

            double leaderRate = leader.PlaybackRate;
            SyncRate sync = TempoSyncCalculator.RateWithin(
                leader.BaseBpm * leaderRate, s.BaseBpm, SyncRangePercent);
            if (!sync.WithinRange)
            {
                _logger.LogInformation(
                    "Deck slot {Slot} one-shot sync skipped: tempo gap to leader {Leader} exceeds ±{Pct:P0}.",
                    slot, leaderSlot, SyncRangePercent);
                return;
            }

            double targetRate = sync.Rate;
            s.PlaybackRate = targetRate;
            s.PitchPosition = PitchPositionFor(targetRate);
            // Preserve the musical KEY across the tempo match (owner: "match BPM without changing the
            // pitch"): engage key-lock so the rate rides the BASS_FX time-stretch path, not the vinyl
            // pitch. Engage BEFORE SetDeckRate (mirrors SetKeyLock's order) so the new rate takes the
            // key-locked path. Left ON afterward — the deck is now tempo-stretched; the KEY LOCK button
            // lights (handler re-emits its feedback) so the deck never lies about its state.
            if (!s.KeyLocked)
            {
                s.KeyLocked = true;
                _backend.SetDeckKeyLock(deck.Handle, true);
            }
            _backend.SetDeckRate(deck.Handle, targetRate);
            PhaseAlignToLeader(slot);
            _logger.LogInformation(
                "Deck slot {Slot} one-shot synced to leader {Leader}: follower base {FollowerBpm:F1}, " +
                "leader effective {LeaderBpm:F1} -> follower rate {Rate:F5} (now {Effective:F1} BPM).",
                slot,
                leaderSlot,
                s.BaseBpm,
                leader.BaseBpm * leaderRate,
                targetRate,
                s.BaseBpm * targetRate);
        }
    }

    public bool IsSyncLocked(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _slots[slot].SyncLocked;
    }

    public void SetSyncLock(int slot, bool enabled)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            if (enabled)
            {
                ReleaseOtherSyncLocks(slot);
                int leaderSlot = ValidLeaderSlot(slot);
                if (leaderSlot < 0)
                {
                    _logger.LogInformation(
                        "Deck slot {Slot} sync engage skipped: no live playing leader deck.", slot);
                    ReleaseSyncLocked(slot);
                    goto Flush;
                }

                DeckSlot s = _slots[slot];
                s.SyncLocked = true;
                // Top-level Sync must preserve the musical key, just like one-shot Sync: engage key-lock
                // before the beatmatched rate is applied so the backend takes the pitch-preserving path.
                if (s.Deck is { } deck && !s.KeyLocked)
                {
                    s.KeyLocked = true;
                    _backend.SetDeckKeyLock(deck.Handle, true);
                }
                // Professional SYNC engage (doc 11): (1) beatmatch tempo to the master, then (2) in BeatLock
                // mode snap the beat phase onto the master grid once so it lands inside the lock zone
                // immediately (no long audible pitch-ride); the continuous loop then holds it there. In
                // TempoOnly mode (SYNC-BEHAVIOR-SPEC §4) skip the snap — the deck tracks tempo only and the
                // DJ owns the phase.
                ReapplyRate(slot);
                if (s.SyncMode == SyncMode.BeatLock && ValidLeaderSlot(slot) >= 0)
                    PhaseAlignToLeader(slot);
                // ReapplyRate reports OutOfRange when the tempo gap is too wide to beatmatch; don't clobber
                // that with Active (it would flash "settling" for a deck that can never lock until the next
                // UpdateSync tick re-derives it — misleading, and worse while paused pre-fade). Only a deck
                // that is actually in range starts Active; the loop refines it to Locked on the next tick.
                if (s.SyncState != SyncLockState.OutOfRange)
                    SetSyncStateLocked(slot, SyncLockState.Active);
            }
            else
            {
                ReleaseSyncLocked(slot);
            }
        }
Flush:
        FlushSyncTransitions();
    }

    public int? SyncMaster
    {
        get { lock (_gate) return ComputeSyncMasterLocked(); }
    }

    public SyncLockState SyncState(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _slots[slot].SyncState;
    }

    /// <inheritdoc />
    public void UpdateSync(long hostTimeTicks)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            // Hold every engaged (slave) deck phase-locked to its master. Normally just one deck is the
            // slave; if a deck has Sync armed but has no valid master yet (the other deck unloaded), it
            // stays Active but uncorrected — never a wrong tempo.
            for (int slot = 0; slot < Decks; slot++)
            {
                if (!_slots[slot].SyncLocked)
                    continue;
                int leader = ValidLeaderSlot(slot);
                if (leader < 0)
                {
                    ReleaseSyncLocked(slot);
                    continue;
                }
                CorrectSlaveLocked(slot, leader);
            }
        }
        // Also flushes any transition queued by a path with no flush of its own (a load / unload), within
        // one pump tick (<=16ms).
        FlushSyncTransitions();
    }

    /// <inheritdoc />
    public bool TryGetSyncMasterBeat(out double effectiveBpm, out double continuousBeat)
    {
        effectiveBpm = 0.0;
        continuousBeat = 0.0;
        lock (_gate)
        {
            if (_disposed || ComputeSyncMasterLocked() is not int masterSlot || _slots[masterSlot].Deck is not { } deck)
                return false;

            DeckSlot master = _slots[masterSlot];
            double bpm = EffectiveBpm(masterSlot);
            if (bpm <= 0.0)
                return false;

            // Continuous beat position from the master deck's true playhead — the deterministic grid the
            // shared clock (and the visuals) lock to. Latency-compensated so the published grid matches
            // what the listener hears.
            double posSeconds = _backend.GetDeckPositionSeconds(deck.Handle) - _phaseLock.OutputLatencySeconds;
            effectiveBpm = bpm;
            continuousBeat = (posSeconds - master.FirstBeat) / (60.0 / master.BaseBpm);
            return true;
        }
    }

    // Caller holds _gate. The sync master is the valid leader of whichever deck currently has Sync
    // engaged (the slave). Computed rather than stored so it stays correct across loads/unloads. Null
    // when no deck is synced or the would-be master is not a valid reference.
    private int? ComputeSyncMasterLocked()
    {
        for (int slot = 0; slot < Decks; slot++)
        {
            if (!_slots[slot].SyncLocked)
                continue;
            int leader = ValidLeaderSlot(slot);
            if (leader >= 0)
                return leader;
        }
        return null;
    }

    // Caller holds _gate. The other deck if it is a valid LIVE sync reference for this slot (loaded,
    // playing, not itself synced, known base BPM) and this slot can be matched (loaded, known base BPM);
    // otherwise -1. A paused deck is not a Sync master: its playhead is static, so locking to it wedges
    // the follower against a frozen grid.
    private int ValidLeaderSlot(int slot)
    {
        DeckSlot s = _slots[slot];
        if (s.Deck is null || s.BaseBpm <= 0.0)
            return -1;
        int leaderSlot = slot == 0 ? 1 : 0;
        DeckSlot leader = _slots[leaderSlot];
        if (leader.Deck is not { Playing: true } || leader.SyncLocked || leader.BaseBpm <= 0.0)
            return -1;
        return leaderSlot;
    }

    // Caller holds _gate. One correction tick for a synced slave: measure the residual beat-phase error
    // against the master, apply the clamped micro pitch-bend, and re-snap once if it has slipped too far.
    private void CorrectSlaveLocked(int slot, int leaderSlot)
    {
        DeckSlot s = _slots[slot];
        DeckSlot leader = _slots[leaderSlot];
        if (s.Deck is not { } deck || leader.Deck is not { } leaderDeck)
            return;

        // A PAUSED (armed) follower holds its cued position — never nudge/resnap it toward the moving master
        // (that would drag the playhead off the DJ's cue point). It re-aligns ONCE at PlayPause (armed start,
        // SYNC-BEHAVIOR-SPEC §6). Reported Active (engaged, armed) until it plays — but a paused deck whose
        // tempo gap is too wide keeps its OutOfRange "can't sync".
        if (!deck.Playing)
        {
            if (s.SyncState != SyncLockState.OutOfRange)
                SetSyncStateLocked(slot, SyncLockState.Active);
            return;
        }

        if (s.BaseBpm <= 0.0 || leader.BaseBpm <= 0.0)
        {
            SetSyncStateLocked(slot, SyncLockState.Active);
            return;
        }

        // Latency-compensated positions. The same output latency is subtracted from both decks, so for
        // deck-to-deck phase it cancels (they share one output path) — kept explicit for correctness and
        // for any future split routing; it primarily aligns the shared clock / visuals to audible output.
        double lat = _phaseLock.OutputLatencySeconds;
        // Position and first-beat are source-media coordinates. Their grid spacing is therefore the
        // analyzed base BPM; playback rate changes how quickly the playhead crosses that grid, not the
        // distance between kick markers in the source.
        double slavePosition = _backend.GetDeckPositionSeconds(deck.Handle) - lat;
        double masterPosition = _backend.GetDeckPositionSeconds(leaderDeck.Handle) - lat;
        var slavePhase = new DeckPhase(
            slavePosition, LocalKickAnchor(s, slavePosition), s.BaseBpm);
        var masterPhase = new DeckPhase(
            masterPosition, LocalKickAnchor(leader, masterPosition), leader.BaseBpm);

        // Too wide a tempo gap to beatmatch: don't run the phase loop (it would chase an unreachable grid);
        // hold the deck's own rate and report OutOfRange so the UI shows "can't sync".
        SyncRate sr = SyncRateFor(slot);
        if (!sr.WithinRange)
        {
            _backend.SetDeckRate(deck.Handle, s.PlaybackRate);
            SetSyncStateLocked(slot, SyncLockState.OutOfRange);
            return;
        }

        // Tempo-only: hold the beatmatched tempo but run NO phase correction or re-snap. Three ways in — the
        // DJ chose Tempo Sync (§4), the follower's own grid failed the confidence gate, or the LEADER's did
        // (§7). The leader matters just as much: the anchor the follower is aligned onto is built from the
        // leader, so locking onto a refused leader imports its error wholesale — the gate was one-sided and
        // read false on the very deck that was the master. Either way the follower tracks the master's tempo
        // and the DJ owns the phase. Reported Active (engaged, tempo held) rather than progressing to Locked.
        if (!s.PhaseSyncReady || !leader.PhaseSyncReady || s.SyncMode == SyncMode.TempoOnly)
        {
            _backend.SetDeckRate(deck.Handle, sr.Rate);
            SetSyncStateLocked(slot, SyncLockState.Active);
            // Every other declined-sync path logs; this one did not, so a downgrade was invisible even in
            // the log. Name which side closed the gate — that is what the DJ has to act on.
            if (!s.PhaseSyncReady || !leader.PhaseSyncReady)
            {
                _logger.LogInformation(
                    "Deck slot {Slot} sync is tempo-only: grid uncertain on {Side}.",
                    slot,
                    !s.PhaseSyncReady && !leader.PhaseSyncReady ? "both decks"
                        : !s.PhaseSyncReady ? "this deck" : "the leader");
            }

            return;
        }

        double beatmatchedRate = sr.Rate; // the tempo-matched base rate, before phase correction
        // Pass the deck's prior lock state so the controller's lock-zone hysteresis can hold a settled deck
        // Locked across the boundary instead of chattering Locked↔Active each tick.
        PhaseLockCorrection correction =
            PhaseLockController.Correct(slavePhase, masterPhase, beatmatchedRate, _phaseLock, s.SyncState);

        _backend.SetDeckRate(deck.Handle, correction.EffectiveRate);

        if (correction.RequiresReSnap)
        {
            double length = _backend.GetDeckLengthSeconds(deck.Handle);
            if (length > 0.0)
            {
                // Seek from the RAW playhead, not the latency-compensated phase base. The -lat term is
                // valid only for the deck-to-deck error MEASUREMENT (where it cancels); used as an
                // absolute seek target it would land the deck OutputLatencySeconds behind the beat.
                // ReSnapSeconds already encodes the correct signed move. Mirrors PhaseAlignToLeader.
                // (doc 27 medium — now live because production OutputLatencySeconds is non-zero.)
                double rawPosition = _backend.GetDeckPositionSeconds(deck.Handle);
                double target = Math.Clamp((rawPosition + correction.ReSnapSeconds) / length, 0.0, 1.0);
                _backend.SetDeckPositionFraction(deck.Handle, target);
            }
        }

        SetSyncStateLocked(slot, correction.State);
    }

    /// <inheritdoc />
    public event Action<int, SyncLockState>? SyncStateChanged;

    // Sync-state transitions queued under _gate (by SetSyncStateLocked) to be raised AFTER the lock is
    // released — a SyncStateChanged handler may do MIDI I/O or marshal to the UI thread, so it must never
    // run nested under the audio-contended _gate (mirrors the DeckEnded pattern).
    private readonly List<(int Slot, SyncLockState State)> _pendingSyncTransitions = new();

    // Caller holds _gate. Store the slot's sync state, logging only on a transition (never per frame) so
    // set diagnostics capture lock/drift changes without flooding the log (doc 03 invariant). The
    // transition is queued for SyncStateChanged and raised once the caller drains + releases the lock, so
    // the LED / UI indicator follows the live state by push instead of a per-frame poll of the engine.
    private void SetSyncStateLocked(int slot, SyncLockState state)
    {
        DeckSlot s = _slots[slot];
        if (s.SyncState == state)
            return;
        _logger.LogInformation("Deck slot {Slot} sync state {Old} -> {New}.", slot, s.SyncState, state);
        s.SyncState = state;
        _pendingSyncTransitions.Add((slot, state));
    }

    // Serializes the drain-and-raise phase across threads. Without it, two flushers (the pump tick and a
    // UI-thread SetSyncLock) can each drain their own batch, release _gate, and then race the raises —
    // delivering a STALE transition after a fresher one (e.g. Off then a late Locked), wedging the SYNC
    // indicator on a state the engine already left. Holding this lock across drain+raise means whoever
    // drains first also raises first, so subscribers always see transitions in queue order. Lock order is
    // _syncRaiseGate -> _gate (flush is only ever called with _gate released), never the reverse.
    private readonly object _syncRaiseGate = new();

    // Drain and raise any queued sync transitions, in order. MUST be called with _gate released (public
    // entry points call it after their lock block). The no-transition path allocates nothing. Subscribers
    // may do MIDI I/O / UI marshaling, so they run outside _gate; a misbehaving subscriber is logged,
    // never bubbled onto the clock-pump / UI thread (global #16/#26).
    private void FlushSyncTransitions()
    {
        lock (_syncRaiseGate)
        {
            List<(int Slot, SyncLockState State)>? transitions;
            lock (_gate)
            {
                if (_pendingSyncTransitions.Count == 0)
                    return;
                transitions = new List<(int, SyncLockState)>(_pendingSyncTransitions);
                _pendingSyncTransitions.Clear();
            }

            foreach ((int slot, SyncLockState state) in transitions)
            {
                try
                {
                    SyncStateChanged?.Invoke(slot, state);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "A SyncStateChanged handler threw for deck slot {Slot}.", slot);
                }
            }
        }
    }

    // Caller holds _gate. Beatmatch one synced deck to the sync leader: leader = the other deck if it is
    // loaded, not itself sync-locked, and has a known base BPM. With no valid leader (or this deck's own
    // base BPM unknown) the rate is left unchanged — Sync stays armed but silent, never a wrong tempo.
    private void ReapplyRate(int slot)
    {
        DeckSlot s = _slots[slot];
        if (s.Deck is not { } deck)
            return;
        if (s.BaseBpm <= 0.0)
        {
            _logger.LogInformation("Deck slot {Slot} sync: own BPM unknown; sync released.", slot);
            ReleaseSyncLocked(slot);
            return;
        }

        if (ValidLeaderSlot(slot) < 0)
        {
            _logger.LogInformation("Deck slot {Slot} sync: no live playing leader; sync released.", slot);
            ReleaseSyncLocked(slot);
            return;
        }

        // Sync may stretch beyond the manual pitch fader (key-lock preserves pitch), but only to the sync
        // ceiling. Too wide a gap reports OutOfRange and holds the deck's own rate rather than a chipmunk pitch.
        SyncRate sr = SyncRateFor(slot);
        if (!sr.WithinRange)
        {
            _logger.LogInformation(
                "Deck slot {Slot} sync: tempo gap exceeds ±{Pct:P0}; holding own rate (OutOfRange).",
                slot, SyncRangePercent);
            _backend.SetDeckRate(deck.Handle, s.PlaybackRate);
            SetSyncStateLocked(slot, SyncLockState.OutOfRange);
            return;
        }
        _backend.SetDeckRate(deck.Handle, sr.Rate);
    }

    // Caller holds _gate. A leader-tempo change (load / base BPM / pitch) must pull every synced deck.
    private void ReapplySyncedFollowers()
    {
        for (int slot = 0; slot < Decks; slot++)
            if (_slots[slot].SyncLocked)
                ReapplyRate(slot);
    }

    private void ReleaseOtherSyncLocks(int slot)
    {
        for (int other = 0; other < Decks; other++)
            if (other != slot && _slots[other].SyncLocked)
                ReleaseSyncLocked(other);
    }

    private void ReleaseSyncLocked(int slot)
    {
        DeckSlot s = _slots[slot];
        s.SyncLocked = false;
        if (s.Deck is { } deck)
            _backend.SetDeckRate(deck.Handle, s.PlaybackRate);
        SetSyncStateLocked(slot, SyncLockState.Off);
    }

    public bool IsQuantizeEnabled(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _slots[slot].Quantize;
    }

    public void SetQuantize(int slot, bool enabled)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            _slots[slot].Quantize = enabled;
            // Phase match (doc 11): enabling Quantize snaps the deck's beat phase onto the leader's grid
            // once, now. The latch stays so a UI/LED reflects the armed state; the alignment is the action.
            if (enabled)
                PhaseAlignToLeader(slot);
        }
    }

    // Caller holds _gate. Snap one deck's playhead so its beat phase lines up with the sync leader's grid.
    // Leader = the other deck if it is loaded with a known anchor + BPM. With no valid leader (or this
    // deck's own anchor/BPM unknown) the playhead is left where it is — Quantize arms but does not guess.
    private void PhaseAlignToLeader(int slot)
    {
        DeckSlot s = _slots[slot];
        if (s.Deck is not { } deck)
            return;

        // Grid-confidence gate (SYNC-BEHAVIOR-SPEC §7): never phase-align onto an untrustworthy grid — a
        // confident-but-wrong snap is worse than tempo-only. Applies to every phase snap (one-shot Beat
        // Sync, Sync Lock engage, Quantize). The Tempo-Sync *mode* gate is at the latch call sites, not
        // here, so a one-shot DeckSyncOnce still snaps regardless of the deck's latch mode.
        if (!s.PhaseSyncReady)
        {
            _logger.LogInformation("Deck slot {Slot} phase align skipped: grid uncertain (tempo-only).", slot);
            return;
        }

        if (s.BaseBpm <= 0.0)
        {
            _logger.LogInformation("Deck slot {Slot} quantize: own tempo unknown; phase unchanged.", slot);
            return;
        }

        // NOT gated on "has an anchor": DeckSlot.FirstBeat documents 0.0 as "unknown", but the analyzer's
        // WrapToBeat legitimately RETURNS 0.0 for a track whose phase lands on the file start, so refusing
        // on 0.0 would falsely strip phase sync from real tracks. The two cases are genuinely
        // indistinguishable in the current type, and separating them needs a nullable anchor plumbed
        // through DeckSlot, the action and the MIDI codec — deliberately out of scope here.
        DeckSlot leader = _slots[slot == 0 ? 1 : 0];
        if (leader.Deck is not { } leaderDeck || leader.BaseBpm <= 0.0)
        {
            _logger.LogInformation("Deck slot {Slot} quantize: no valid leader; phase unchanged.", slot);
            return;
        }

        // The leader supplies the anchor, so its grid must be vouched for too — see CorrectSlaveLocked.
        if (!leader.PhaseSyncReady)
        {
            _logger.LogInformation(
                "Deck slot {Slot} phase align skipped: the leader's grid is uncertain (tempo-only).", slot);
            return;
        }

        // Deck positions and anchors are measured in source-media seconds, so phase must use each
        // track's analyzed base BPM. Effective BPM describes wall-clock playback speed and would skew
        // the kick grid whenever Sync changes the deck rate.
        double followerPosition = _backend.GetDeckPositionSeconds(deck.Handle);
        double leaderPosition = _backend.GetDeckPositionSeconds(leaderDeck.Handle);
        var followerPhase = new DeckPhase(
            followerPosition, LocalKickAnchor(s, followerPosition), s.BaseBpm);
        var leaderPhase = new DeckPhase(
            leaderPosition, LocalKickAnchor(leader, leaderPosition), leader.BaseBpm);

        // Snap onto the leader's DOWNBEAT when BOTH bar anchors are known: a beat-level snap can land
        // beat 3 on the leader's one — audibly locked but musically a bar off. With either downbeat
        // unknown (analysis ambiguity is confidence-gated at the source), the beat-level snap stands.
        // The continuous lock (CorrectSlaveLocked) stays beat-based, which preserves bar alignment.
        //
        // Bar-snap only when the follower is NOT playing (armed pre-fade, the normal moment to align).
        // BarPhaseNudgeSeconds wraps to +/-half a BAR (+/-2 beats at 4/4) vs the beat snap's +/-half beat,
        // so bar-snapping a deck already audible in the mix could jump the playhead up to ~2 beats — an
        // audible skip. A playing follower therefore keeps the near-inaudible beat-level snap.
        bool barSnap = s.Downbeat > 0.0 && leader.Downbeat > 0.0 && !deck.Playing;
        double nudgeSeconds = barSnap
            ? PhaseAlignmentCalculator.BarPhaseNudgeSeconds(
                followerPhase with { FirstBeatSeconds = s.Downbeat },
                leaderPhase with { FirstBeatSeconds = leader.Downbeat },
                BeatsPerBar)
            : PhaseAlignmentCalculator.PhaseNudgeSeconds(followerPhase, leaderPhase);
        double length = _backend.GetDeckLengthSeconds(deck.Handle);
        if (length <= 0.0)
            return;

        double targetFraction = Math.Clamp((followerPhase.PositionSeconds + nudgeSeconds) / length, 0.0, 1.0);
        _backend.SetDeckPositionFraction(deck.Handle, targetFraction);
        _logger.LogInformation(
            "Deck slot {Slot} quantize: {Grid}-aligned by {Nudge:F4}s to the leader grid.",
            slot, barSnap ? "bar" : "beat", nudgeSeconds);
    }

    private static double LocalKickAnchor(DeckSlot slot, double positionSeconds)
    {
        double[] kicks = slot.KickOnsets;
        if (slot.BaseBpm <= 0.0 || kicks.Length == 0)
            return slot.FirstBeat;

        double nearest = NearestSorted(kicks, positionSeconds);
        double beatSeconds = 60.0 / slot.BaseBpm;
        double anchor = nearest % beatSeconds;
        return anchor < 0.0 ? anchor + beatSeconds : anchor;
    }

    private static double NearestSorted(double[] values, double target)
    {
        int index = Array.BinarySearch(values, target);
        if (index >= 0)
            return values[index];

        int next = ~index;
        if (next <= 0)
            return values[0];
        if (next >= values.Length)
            return values[^1];

        double before = values[next - 1];
        double after = values[next];
        return Math.Abs(target - before) <= Math.Abs(after - target) ? before : after;
    }

    // ponytail: DownbeatEstimator is only ever invoked in 4/4 today (BpmDetector passes no meter), so the
    // action layer carries no beats-per-bar; thread BpmResult.BeatsPerBar through DeckSetDownbeat when
    // non-4/4 support lands.
    private const int BeatsPerBar = 4;
}
