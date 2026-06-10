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
        lock (_gate) return _slots[slot].PitchPosition;
    }

    public void SetPitch(int slot, double value, bool relative)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            DeckSlot s = _slots[slot];
            double next = Math.Clamp(relative ? s.PitchPosition + value : value, 0.0, 1.0);
            s.PitchPosition = next;
            s.PlaybackRate = RateFor(next);
            // While Sync is engaged the synced rate owns the deck (doc 11: Sync is an assist; manual
            // nudging of a synced deck is a later increment). The position is still stored so it takes
            // effect the moment Sync is released.
            if (s.Deck is { } deck && !s.SyncLocked)
                _backend.SetDeckRate(deck.Handle, s.PlaybackRate);
            // This deck may be the sync leader — pull any synced follower to the new tempo.
            ReapplySyncedFollowers();
        }
    }

    public double DeckBaseBpm(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _slots[slot].BaseBpm;
    }

    public void SetDeckBaseBpm(int slot, double bpm)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            _slots[slot].BaseBpm = bpm > 0.0 ? bpm : 0.0;
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
        lock (_gate)
        {
            double baseBpm = _slots[slot].BaseBpm;
            return baseBpm > 0.0 ? baseBpm * (1.0 - PitchRangePercent) : 0.0;
        }
    }

    public double MaximumDeckBpm(int slot)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            double baseBpm = _slots[slot].BaseBpm;
            return baseBpm > 0.0 ? baseBpm * (1.0 + PitchRangePercent) : 0.0;
        }
    }

    public void SetDeckBpm(int slot, double bpm)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            DeckSlot s = _slots[slot];
            if (s.BaseBpm <= 0.0 || bpm <= 0.0)
                return;

            s.PitchPosition = PitchPositionFor(bpm / s.BaseBpm);
            s.PlaybackRate = RateFor(s.PitchPosition);
            if (s.Deck is { } deck && !s.SyncLocked)
                _backend.SetDeckRate(deck.Handle, s.PlaybackRate);
            ReapplySyncedFollowers();
        }
    }

    // Caller holds _gate. A one-shot sync may exceed the manual pitch fader's display range, so audible
    // tempo follows the separately retained playback rate. 0 when base BPM is unknown.
    private double EffectiveBpm(int slot)
    {
        DeckSlot s = _slots[slot];
        if (s.BaseBpm <= 0.0)
            return 0.0;
        double rate = s.SyncLocked ? SyncedRateFor(slot) : s.PlaybackRate;
        return s.BaseBpm * rate;
    }

    // Caller holds _gate. The rate Sync would apply to a follower deck (its leader's audible tempo folded
    // to the nearest octave), or its manual pitch rate when no valid leader exists — mirrors ReapplyRate.
    private double SyncedRateFor(int slot)
    {
        DeckSlot s = _slots[slot];
        DeckSlot leader = _slots[slot == 0 ? 1 : 0];
        if (s.BaseBpm <= 0.0 || leader.Deck is null || leader.SyncLocked || leader.BaseBpm <= 0.0)
            return s.PlaybackRate;
        double leaderEffectiveBpm = leader.BaseBpm * leader.PlaybackRate;
        return TempoSyncCalculator.RateFor(leaderEffectiveBpm, s.BaseBpm);
    }
}
