namespace Liveolator.Core.Audio;

/// <summary>
/// The render-loop pump for the continuous beat-sync correction loop — the deck-engine counterpart of
/// <c>IManualBeatClockDriver</c>. The host loop calls <see cref="UpdateSync"/> each tick to hold the
/// synced (slave) deck phase-locked to the master, and <see cref="TryGetSyncMasterBeat"/> to drive the
/// shared <c>DeckDrivenBeatClock</c> off the master deck's live grid.
/// </summary>
/// <remarks>
/// A separate seam (not <see cref="IMultiDeckPlaybackEngine"/>) so the Live view-model depends only on a
/// pump abstraction, never on "the engine" — the UI stays an action source (doc 04 iron rule). The
/// two-deck BASS engine implements it; headless/single-deck composition leaves it null.
/// </remarks>
public interface ISyncCorrectionDriver
{
    /// <summary>
    /// Advance the phase-lock correction loop to <paramref name="hostTimeTicks"/>: measure the slave's
    /// residual beat-phase error against the master and apply a small clamped rate correction so it
    /// stays locked over time (no drift). Host-time-stamped so jitter in the pump cannot cause drift.
    /// No-op when no deck is synced.
    /// </summary>
    void UpdateSync(long hostTimeTicks);

    /// <summary>
    /// The sync master's live musical state for driving the shared beat clock: its effective (audible)
    /// tempo in BPM and its continuous beat position
    /// (<c>(positionSeconds − firstBeatSeconds) / (60 / effectiveBpm)</c>). Returns false when there is
    /// no sync master (the caller then idles the deck clock).
    /// </summary>
    bool TryGetSyncMasterBeat(out double effectiveBpm, out double continuousBeat);
}
