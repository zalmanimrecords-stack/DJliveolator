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
        lock (_gate) return _firstBeat[slot];
    }

    public void SetDeckFirstBeat(int slot, double firstBeatSeconds)
    {
        ValidateSlot(slot);
        lock (_gate) _firstBeat[slot] = firstBeatSeconds > 0.0 ? firstBeatSeconds : 0.0;
    }

    public void SyncOnce(int slot)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            if (_decks[slot] is not { } deck || _baseBpm[slot] <= 0.0)
                return;

            int leader = slot == 0 ? 1 : 0;
            if (_decks[leader] is null || _baseBpm[leader] <= 0.0)
                return;

            double leaderRate = _playbackRate[leader];
            double targetRate = TempoSyncCalculator.RateFor(
                _baseBpm[leader] * leaderRate,
                _baseBpm[slot]);
            _playbackRate[slot] = targetRate;
            _pitchPosition[slot] = PitchPositionFor(targetRate);
            _backend.SetDeckRate(deck.Handle, targetRate);
            PhaseAlignToLeader(slot);
            _logger.LogInformation(
                "Deck slot {Slot} one-shot synced to deck {Leader} at rate {Rate:F5}.",
                slot,
                leader,
                targetRate);
        }
    }

    public bool IsSyncLocked(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _syncLocked[slot];
    }

    public void SetSyncLock(int slot, bool enabled)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            _syncLocked[slot] = enabled;
            if (enabled)
            {
                // Professional SYNC engage (doc 11): (1) beatmatch tempo to the master, then (2) snap the
                // beat phase onto the master grid once so it lands inside the lock zone immediately (no
                // long audible pitch-ride). The continuous loop (UpdateSync) then holds it there.
                ReapplyRate(slot);
                if (ValidLeaderSlot(slot) >= 0)
                    PhaseAlignToLeader(slot);
                SetSyncStateLocked(slot, SyncLockState.Active); // the loop refines to Locked on the next tick
            }
            else
            {
                if (_decks[slot] is { } deck)
                    _backend.SetDeckRate(deck.Handle, _playbackRate[slot]);
                SetSyncStateLocked(slot, SyncLockState.Off);
            }
        }
    }

    public int? SyncMaster
    {
        get { lock (_gate) return ComputeSyncMasterLocked(); }
    }

    public SyncLockState SyncState(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _syncState[slot];
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
                if (!_syncLocked[slot])
                    continue;
                int leader = ValidLeaderSlot(slot);
                if (leader < 0)
                {
                    SetSyncStateLocked(slot, SyncLockState.Active);
                    continue;
                }
                CorrectSlaveLocked(slot, leader);
            }
        }
    }

    /// <inheritdoc />
    public bool TryGetSyncMasterBeat(out double effectiveBpm, out double continuousBeat)
    {
        effectiveBpm = 0.0;
        continuousBeat = 0.0;
        lock (_gate)
        {
            if (_disposed || ComputeSyncMasterLocked() is not int master || _decks[master] is not { } deck)
                return false;

            double bpm = EffectiveBpm(master);
            if (bpm <= 0.0)
                return false;

            // Continuous beat position from the master deck's true playhead — the deterministic grid the
            // shared clock (and the visuals) lock to. Latency-compensated so the published grid matches
            // what the listener hears.
            double posSeconds = _backend.GetDeckPositionSeconds(deck.Handle) - _phaseLock.OutputLatencySeconds;
            effectiveBpm = bpm;
            continuousBeat = (posSeconds - _firstBeat[master]) / (60.0 / _baseBpm[master]);
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
            if (!_syncLocked[slot])
                continue;
            int leader = ValidLeaderSlot(slot);
            if (leader >= 0)
                return leader;
        }
        return null;
    }

    // Caller holds _gate. The other deck if it is a valid sync reference for this slot (loaded, not itself
    // synced, known base BPM) and this slot can be matched (loaded, known base BPM); otherwise -1.
    private int ValidLeaderSlot(int slot)
    {
        if (_decks[slot] is null || _baseBpm[slot] <= 0.0)
            return -1;
        int leader = slot == 0 ? 1 : 0;
        if (_decks[leader] is null || _syncLocked[leader] || _baseBpm[leader] <= 0.0)
            return -1;
        return leader;
    }

    // Caller holds _gate. One correction tick for a synced slave: measure the residual beat-phase error
    // against the master, apply the clamped micro pitch-bend, and re-snap once if it has slipped too far.
    private void CorrectSlaveLocked(int slot, int leader)
    {
        if (_decks[slot] is not { } deck || _decks[leader] is not { } leaderDeck)
            return;

        if (_baseBpm[slot] <= 0.0 || _baseBpm[leader] <= 0.0)
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
        var slavePhase = new DeckPhase(
            _backend.GetDeckPositionSeconds(deck.Handle) - lat, _firstBeat[slot], _baseBpm[slot]);
        var masterPhase = new DeckPhase(
            _backend.GetDeckPositionSeconds(leaderDeck.Handle) - lat, _firstBeat[leader], _baseBpm[leader]);

        double beatmatchedRate = SyncedRateFor(slot); // the tempo-matched base rate, before phase correction
        PhaseLockCorrection correction =
            PhaseLockController.Correct(slavePhase, masterPhase, beatmatchedRate, _phaseLock);

        _backend.SetDeckRate(deck.Handle, correction.EffectiveRate);

        if (correction.RequiresReSnap)
        {
            double length = _backend.GetDeckLengthSeconds(deck.Handle);
            if (length > 0.0)
            {
                double target = Math.Clamp((slavePhase.PositionSeconds + correction.ReSnapSeconds) / length, 0.0, 1.0);
                _backend.SetDeckPositionFraction(deck.Handle, target);
            }
        }

        SetSyncStateLocked(slot, correction.State);
    }

    // Caller holds _gate. Store the slot's sync state, logging only on a transition (never per frame) so
    // set diagnostics capture lock/drift changes without flooding the log (doc 03 invariant).
    private void SetSyncStateLocked(int slot, SyncLockState state)
    {
        if (_syncState[slot] == state)
            return;
        _logger.LogInformation("Deck slot {Slot} sync state {Old} -> {New}.", slot, _syncState[slot], state);
        _syncState[slot] = state;
    }

    // Caller holds _gate. Beatmatch one synced deck to the sync leader: leader = the other deck if it is
    // loaded, not itself sync-locked, and has a known base BPM. With no valid leader (or this deck's own
    // base BPM unknown) the rate is left unchanged — Sync stays armed but silent, never a wrong tempo.
    private void ReapplyRate(int slot)
    {
        if (_decks[slot] is not { } deck)
            return;
        if (_baseBpm[slot] <= 0.0)
        {
            _logger.LogInformation("Deck slot {Slot} sync: own BPM unknown; rate unchanged.", slot);
            return;
        }

        int leader = slot == 0 ? 1 : 0;
        if (_decks[leader] is null || _syncLocked[leader] || _baseBpm[leader] <= 0.0)
        {
            _logger.LogInformation("Deck slot {Slot} sync: no valid leader; rate unchanged.", slot);
            return;
        }

        // A prior one-shot sync can put the leader beyond the manual pitch fader's display range.
        double leaderEffectiveBpm = _baseBpm[leader] * _playbackRate[leader];
        double rate = TempoSyncCalculator.RateFor(leaderEffectiveBpm, _baseBpm[slot]);
        _backend.SetDeckRate(deck.Handle, rate);
    }

    // Caller holds _gate. A leader-tempo change (load / base BPM / pitch) must pull every synced deck.
    private void ReapplySyncedFollowers()
    {
        for (int slot = 0; slot < Decks; slot++)
            if (_syncLocked[slot])
                ReapplyRate(slot);
    }

    public bool IsQuantizeEnabled(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _quantize[slot];
    }

    public void SetQuantize(int slot, bool enabled)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            _quantize[slot] = enabled;
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
        if (_decks[slot] is not { } deck)
            return;

        if (_baseBpm[slot] <= 0.0)
        {
            _logger.LogInformation("Deck slot {Slot} quantize: own tempo unknown; phase unchanged.", slot);
            return;
        }

        int leader = slot == 0 ? 1 : 0;
        if (_decks[leader] is null || _baseBpm[leader] <= 0.0)
        {
            _logger.LogInformation("Deck slot {Slot} quantize: no valid leader; phase unchanged.", slot);
            return;
        }

        // Deck positions and anchors are measured in source-media seconds, so phase must use each
        // track's analyzed base BPM. Effective BPM describes wall-clock playback speed and would skew
        // the kick grid whenever Sync changes the deck rate.
        var followerPhase = new DeckPhase(
            _backend.GetDeckPositionSeconds(deck.Handle), _firstBeat[slot], _baseBpm[slot]);
        var leaderPhase = new DeckPhase(
            _backend.GetDeckPositionSeconds(_decks[leader]!.Handle), _firstBeat[leader], _baseBpm[leader]);

        double nudgeSeconds = PhaseAlignmentCalculator.PhaseNudgeSeconds(followerPhase, leaderPhase);
        double length = _backend.GetDeckLengthSeconds(deck.Handle);
        if (length <= 0.0)
            return;

        double targetFraction = Math.Clamp((followerPhase.PositionSeconds + nudgeSeconds) / length, 0.0, 1.0);
        _backend.SetDeckPositionFraction(deck.Handle, targetFraction);
        _logger.LogInformation(
            "Deck slot {Slot} quantize: phase-aligned by {Nudge:F4}s to the leader grid.", slot, nudgeSeconds);
    }
}
