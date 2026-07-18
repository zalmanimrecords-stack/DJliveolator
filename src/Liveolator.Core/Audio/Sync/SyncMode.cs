namespace Liveolator.Core.Audio.Sync;

/// <summary>
/// How an engaged sync latch treats a follower deck (SYNC-BEHAVIOR-SPEC §4). Both modes match tempo and
/// keep following the master's tempo changes; they differ only in whether the beat PHASE is aligned and
/// held. The one-shot Beat Sync (a single beatmatch + phase snap that does not latch) is a separate action
/// (<c>DeckSyncOnce</c>), not a latch mode.
/// </summary>
public enum SyncMode
{
    /// <summary>Tempo + phase: beatmatch, snap the beat/bar phase onto the master, and hold it there with
    /// the continuous correction loop. The default SYNC button — "Sync Lock" (owner decision §12-1).</summary>
    BeatLock = 0,

    /// <summary>Tempo only: beatmatch and keep following the master's tempo, but never align or correct the
    /// beat phase — the DJ rides the "one" by hand. For manual/creative (off-beat) mixing, or when the DJ
    /// wants tempo help without the machine touching phase. Same audible behaviour the grid-confidence gate
    /// forces when a grid is untrustworthy (§7), reached here by explicit choice instead.</summary>
    TempoOnly = 1,
}
