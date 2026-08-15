using Liveolator.Audio.Playback;
using Liveolator.Core.Audio.Sync;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// The grid-confidence gate, hardened (SYNC-BEHAVIOR-SPEC §7, 2026-08-15). Two defects measured on a real
/// set drove this:
/// <list type="number">
/// <item><b>It failed OPEN.</b> A slot defaulted to <c>PhaseSyncReady = true</c> and every load reset it
/// to true, so a track whose grid had never been judged — a pre-v12 row, or any track loaded through a
/// path that supplies no verdict — got a confident phase snap onto an anchor derived from the very
/// broadband envelope that measured 37–214 ms wrong. "Unknown" must mean tempo-only, not "assume good":
/// an unnecessary tempo-only downgrade costs far less than a confident-but-wrong lock on a full floor.</item>
/// <item><b>It was one-sided.</b> Only the FOLLOWER's flag was read, yet the anchor the follower aligns
/// onto is built from the LEADER. A refused track playing out with a good track syncing in kept the whole
/// half-beat error, with the gate reading correctly false on the deck that was the master.</item>
/// </list>
/// </summary>
public class TwoDeckBassEngineSyncGateTests
{
    private const int LeaderHandle = 100;
    private const int FollowerHandle = 101;

    private static TwoDeckBassEngine NewEngine(out FakeBassMixerBackend backend)
    {
        backend = new FakeBassMixerBackend();
        return new TwoDeckBassEngine(backend, new BassMixer(deckCount: TwoDeckBassEngine.Decks));
    }

    // Deck 0 plays as leader, deck 1 is the follower, both at 128 BPM, follower a phase error past the
    // re-snap threshold so a confident grid would visibly seek it.
    private static TwoDeckBassEngine TwoPlayingDecks(out FakeBassMixerBackend backend)
    {
        TwoDeckBassEngine engine = NewEngine(out backend);
        engine.Load(0, @"C:\a.wav");
        engine.Load(1, @"C:\b.wav");
        engine.SetDeckBaseBpm(0, 128.0);
        engine.SetDeckBaseBpm(1, 128.0);
        engine.PlayPause(0);
        engine.PlayPause(1);
        backend.PositionFraction[LeaderHandle] = 0.0;
        backend.PositionFraction[FollowerHandle] = 0.003; // ≈0.64 beat — past the 0.25-beat re-snap gate
        return engine;
    }

    [Fact]
    public void ADeckWithNoGridVerdict_IsNotPhaseSyncReady_OnLoadOrReload()
    {
        // Inverted deliberately from the old "default to confident/preserve". A slot that has never been
        // told anything about its grid must not offer a phase lock.
        using TwoDeckBassEngine engine = NewEngine(out _);
        engine.Load(0, @"C:\a.wav");
        Assert.False(engine.DeckPhaseSyncReady(0));

        engine.SetDeckPhaseSyncReady(0, true);
        engine.Load(0, @"C:\b.wav"); // grid confidence is per-track: the new track has no verdict yet

        Assert.False(engine.DeckPhaseSyncReady(0));
    }

    [Fact]
    public void Sync_OnADeckWithNoGridVerdict_TempoMatchesWithoutPhaseAligning()
    {
        using TwoDeckBassEngine engine = TwoPlayingDecks(out FakeBassMixerBackend backend);
        engine.SetDeckPhaseSyncReady(0, true);   // the leader is vouched for; the follower was never judged

        engine.SetSyncLock(1, true);
        engine.UpdateSync(0);

        Assert.Equal(1.0, backend.Rate[FollowerHandle], 6);                 // tempo IS matched
        Assert.Equal(0.003, backend.PositionFraction[FollowerHandle], 6);   // phase left alone
        Assert.Equal(SyncLockState.Active, engine.SyncState(1));            // engaged, never Locked
    }

    [Fact]
    public void Sync_ToALeaderWhoseGridWasRefused_TempoMatchesWithoutPhaseAligning()
    {
        // The follower's own grid is fine. The LEADER's was refused — and the leader is where the anchor
        // comes from, so aligning to it would import exactly the error the gate exists to prevent.
        using TwoDeckBassEngine engine = TwoPlayingDecks(out FakeBassMixerBackend backend);
        engine.SetDeckPhaseSyncReady(1, true);
        engine.SetDeckPhaseSyncReady(0, false);

        engine.SetSyncLock(1, true);
        engine.UpdateSync(0);

        Assert.Equal(1.0, backend.Rate[FollowerHandle], 6);
        Assert.Equal(0.003, backend.PositionFraction[FollowerHandle], 6);
        Assert.Equal(SyncLockState.Active, engine.SyncState(1));
    }

    [Fact]
    public void Sync_WithBothGridsVouched_StillPhaseAligns()
    {
        // The other side of the gate: tightening it must not break the case it is meant to allow, or the
        // suite would pass just as well with sync disabled entirely.
        using TwoDeckBassEngine engine = TwoPlayingDecks(out FakeBassMixerBackend backend);
        engine.SetDeckPhaseSyncReady(0, true);
        engine.SetDeckPhaseSyncReady(1, true);

        engine.SetSyncLock(1, true);
        engine.UpdateSync(0);

        Assert.NotEqual(0.003, backend.PositionFraction[FollowerHandle]); // it DID re-snap
    }

    [Fact]
    public void ATrackWhoseAnchorWrapsToZero_StillPhaseAligns()
    {
        // Guards a refusal that was tried and REMOVED. Gating on "FirstBeat <= 0 and no kicks" looks like
        // it rejects an unanalyzed deck, but DeckSlot.FirstBeat overloads 0.0 as both "unknown" and "the
        // beat is at the file start" — and BpmDetector publishes WrapToBeat(anchor), which returns exactly
        // 0.0 for a track that starts on the beat. Such a gate silently strips phase sync from real
        // tracks. Splitting the two meanings needs a nullable anchor through DeckSlot, the action and the
        // MIDI codec; until then a zero anchor must be honoured.
        using TwoDeckBassEngine engine = TwoPlayingDecks(out FakeBassMixerBackend backend);
        engine.SetDeckPhaseSyncReady(0, true);
        engine.SetDeckPhaseSyncReady(1, true);
        engine.SetDeckFirstBeat(0, 0.0);
        engine.SetDeckFirstBeat(1, 0.0);

        engine.SetSyncLock(1, true);
        engine.UpdateSync(0);

        Assert.NotEqual(0.003, backend.PositionFraction[FollowerHandle]);
    }
}
