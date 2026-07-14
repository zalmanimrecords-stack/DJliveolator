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
        // ReapplySyncedFollowers can move a follower's lock state (e.g. into/out of OutOfRange); flush the
        // transition here so the indicator follows even in a host with no UpdateSync pump running.
        FlushSyncTransitions();
    }

    public bool IsKeyLockEnabled(int slot)
    {
        ValidateSlot(slot);
        lock (_gate) return _slots[slot].KeyLocked;
    }

    public void SetKeyLock(int slot, bool enabled)
    {
        ValidateSlot(slot);
        lock (_gate)
        {
            DeckSlot s = _slots[slot];
            s.KeyLocked = enabled; // per-deck transport state; persists across loads via DeckSlot

            // Phase 3 (native): switch the backend's audible rate path for the loaded deck, then re-apply
            // the current rate so the change is heard immediately — key-lock on routes the rate through the
            // BASS_FX tempo attribute (pitch preserved), off through vinyl frequency. SetDeckKeyLock must
            // precede SetDeckRate so the rate takes the newly chosen path. Sync, when engaged, owns the
            // rate (mirrors SetPitch), so re-apply only when not sync-locked. With nothing loaded there is
            // no stream to key-lock — the armed state takes effect on the next Load.
            if (s.Deck is { } deck)
            {
                _backend.SetDeckKeyLock(deck.Handle, enabled);
                if (!s.SyncLocked)
                    _backend.SetDeckRate(deck.Handle, s.PlaybackRate);
            }
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
        FlushSyncTransitions(); // see SetPitch — lock-state moves here must not wait for the pump
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
        FlushSyncTransitions(); // see SetPitch — lock-state moves here must not wait for the pump
    }

    public void PitchBend(int slot, double bendFraction)
    {
        ValidateSlot(slot);
        if (!double.IsFinite(bendFraction))
            return;
        lock (_gate)
        {
            DeckSlot s = _slots[slot];
            // Sync owns a locked deck's rate; bending it would fight the sync corrector, so leave it be.
            if (s.Deck is not { } deck || s.SyncLocked)
                return;
            // Apply a transient rate on TOP of the deck's normal rate (0 ⇒ restore). PitchPosition /
            // PlaybackRate are untouched, so the pitch fader and nominal BPM don't move — only the heard
            // rate bends, sliding the phase. The next normal rate change (or release) restores it.
            double rate = bendFraction != 0.0 ? s.PlaybackRate * (1.0 + bendFraction) : s.PlaybackRate;
            _backend.SetDeckRate(deck.Handle, rate);
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

    // Caller holds _gate. The sync rate decision for a follower deck: its leader's audible tempo folded to
    // the nearest octave, capped to the sync stretch ceiling. WithinRange is false when the tempo gap is
    // too wide to beatmatch (the caller surfaces "can't sync" instead of riding an out-of-range pitch).
    // Returns the deck's own manual rate (WithinRange=true) when there is no valid leader — Sync armed but
    // silent, never a wrong tempo. Single source of the sync rate for ReapplyRate / EffectiveBpm / the loop.
    private SyncRate SyncRateFor(int slot)
    {
        DeckSlot s = _slots[slot];
        DeckSlot leader = _slots[slot == 0 ? 1 : 0];
        if (s.BaseBpm <= 0.0 || leader.Deck is null || leader.SyncLocked || leader.BaseBpm <= 0.0)
            return new SyncRate(s.PlaybackRate, WithinRange: true);
        double leaderEffectiveBpm = leader.BaseBpm * leader.PlaybackRate;
        return TempoSyncCalculator.RateWithin(leaderEffectiveBpm, s.BaseBpm, SyncRangePercent);
    }

    // Caller holds _gate. The rate Sync would apply to a follower (capped to the ceiling), or its manual
    // rate when out of range / no leader — so EffectiveBpm reports the deck's true audible tempo either way.
    private double SyncedRateFor(int slot)
    {
        SyncRate sr = SyncRateFor(slot);
        return sr.WithinRange ? sr.Rate : _slots[slot].PlaybackRate;
    }
}
