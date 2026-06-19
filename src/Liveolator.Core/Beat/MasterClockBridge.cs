using Liveolator.Core.Audio;

namespace Liveolator.Core.Beat;

/// <summary>
/// Bridges the sync <em>master</em> deck to the shared beat clock on each render tick — the realisation
/// of the product's audio↔visual lock. It pumps the continuous phase-lock correction loop, and points
/// the shared <see cref="SwitchingBeatClock"/> at the <see cref="DeckDrivenBeatClock"/> (fed from the
/// master deck's live grid) while a deck is the sync master, falling back to the base clock (tap/audio)
/// otherwise. So whenever a deck is synced, the visuals and beat readout follow that deck's beat
/// directly; with nothing synced they follow the previous source unchanged.
/// </summary>
public sealed class MasterClockBridge
{
    private readonly ISyncCorrectionDriver _sync;
    private readonly DeckDrivenBeatClock _deckClock;
    private readonly SwitchingBeatClock _shared;
    private readonly IBeatClock _baseClock;

    /// <param name="sync">The deck engine's correction-loop pump + master-beat source.</param>
    /// <param name="deckClock">The clock driven by the master deck's live grid.</param>
    /// <param name="shared">The subscriber-facing shared clock whose source this bridge switches.</param>
    /// <param name="baseClock">The fallback source used when no deck is the sync master (tap/audio clock).</param>
    public MasterClockBridge(
        ISyncCorrectionDriver sync,
        DeckDrivenBeatClock deckClock,
        SwitchingBeatClock shared,
        IBeatClock baseClock)
    {
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
        _deckClock = deckClock ?? throw new ArgumentNullException(nameof(deckClock));
        _shared = shared ?? throw new ArgumentNullException(nameof(shared));
        _baseClock = baseClock ?? throw new ArgumentNullException(nameof(baseClock));
    }

    /// <summary>Advance the correction loop and update/select the shared clock source for this tick.</summary>
    public void Tick(long hostTimeTicks)
    {
        _sync.UpdateSync(hostTimeTicks);

        // A master with a usable analyzed grid drives the deck clock; otherwise — no sync master, or a
        // master playing an un-analyzed track (BPM unknown / non-positive) — fall back to the base clock
        // so live audio detection takes over instead of showing an idle deck grid.
        if (_sync.TryGetSyncMasterBeat(out double effectiveBpm, out double continuousBeat) && effectiveBpm > 0.0)
        {
            _deckClock.Update(effectiveBpm, continuousBeat, hostTimeTicks);
            _shared.Select(_deckClock);
        }
        else
        {
            _deckClock.Reset();
            _shared.Select(_baseClock);
        }
    }
}
