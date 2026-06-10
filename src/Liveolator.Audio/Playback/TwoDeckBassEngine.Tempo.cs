using Liveolator.Core.Audio.Sync;

namespace Liveolator.Audio.Playback;

/// <summary>
/// Tempo surface of <see cref="TwoDeckBassEngine"/>: the manual pitch fader and the analyzed base /
/// audible effective BPM. Sync engagement (which overrides the manual rate) lives in the Sync partial.
/// </summary>
public sealed partial class TwoDeckBassEngine
{
    public double PitchPosition(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _pitchPosition[slot];
    }

    public void SetPitch(int slot, double value, bool relative)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            double next = Math.Clamp(relative ? _pitchPosition[slot] + value : value, 0.0, 1.0);
            _pitchPosition[slot] = next;
            _playbackRate[slot] = RateFor(next);
            // While Sync is engaged the synced rate owns the deck (doc 11: Sync is an assist; manual
            // nudging of a synced deck is a later increment). The position is still stored so it takes
            // effect the moment Sync is released.
            if (_decks[slot] is { } deck && !_syncLocked[slot])
                _backend.SetDeckRate(deck.Handle, _playbackRate[slot]);
            // This deck may be the sync leader — pull any synced follower to the new tempo.
            ReapplySyncedFollowers();
        }
    }

    public double DeckBaseBpm(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _baseBpm[slot];
    }

    public void SetDeckBaseBpm(int slot, double bpm)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            _baseBpm[slot] = bpm > 0.0 ? bpm : 0.0;
            // A new reference tempo re-beatmatches: this deck may be a leader (pull its followers) or a
            // synced follower whose own tempo just changed.
            ReapplySyncedFollowers();
        }
    }

    public double DeckBpm(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return EffectiveBpm(slot);
    }

    public double MinimumDeckBpm(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _baseBpm[slot] > 0.0 ? _baseBpm[slot] * (1.0 - PitchRangePercent) : 0.0;
    }

    public double MaximumDeckBpm(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _baseBpm[slot] > 0.0 ? _baseBpm[slot] * (1.0 + PitchRangePercent) : 0.0;
    }

    public void SetDeckBpm(int slot, double bpm)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            if (_baseBpm[slot] <= 0.0 || bpm <= 0.0)
                return;

            _pitchPosition[slot] = PitchPositionFor(bpm / _baseBpm[slot]);
            _playbackRate[slot] = RateFor(_pitchPosition[slot]);
            if (_decks[slot] is { } deck && !_syncLocked[slot])
                _backend.SetDeckRate(deck.Handle, _playbackRate[slot]);
            ReapplySyncedFollowers();
        }
    }

    // Caller holds _gate. A one-shot sync may exceed the manual pitch fader's display range, so audible
    // tempo follows the separately retained playback rate. 0 when base BPM is unknown.
    private double EffectiveBpm(int slot)
    {
        if (_baseBpm[slot] <= 0.0)
            return 0.0;
        double rate = _syncLocked[slot] ? SyncedRateFor(slot) : _playbackRate[slot];
        return _baseBpm[slot] * rate;
    }

    // Caller holds _gate. The rate Sync would apply to a follower deck (its leader's audible tempo folded
    // to the nearest octave), or its manual pitch rate when no valid leader exists — mirrors ReapplyRate.
    private double SyncedRateFor(int slot)
    {
        int leader = slot == 0 ? 1 : 0;
        if (_baseBpm[slot] <= 0.0 || _decks[leader] is null || _syncLocked[leader] || _baseBpm[leader] <= 0.0)
            return _playbackRate[slot];
        double leaderEffectiveBpm = _baseBpm[leader] * _playbackRate[leader];
        return TempoSyncCalculator.RateFor(leaderEffectiveBpm, _baseBpm[slot]);
    }
}
