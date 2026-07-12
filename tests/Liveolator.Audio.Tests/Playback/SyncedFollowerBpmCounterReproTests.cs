using Liveolator.Audio.Playback;
using Liveolator.Core.Actions;
using Liveolator.Core.Audio;
using Liveolator.Core.Mixer;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// Repro for the owner's report: after SYNC the two decks' on-screen BPM counters do not read the
/// same value. The on-screen counter is driven ONLY by <see cref="PerformanceActionKind.DeckBpm"/>
/// feedback (DeckViewModel.ApplyBpmFeedback). With continuous Sync Lock engaged (the DeckSyncToggle
/// path a controller maps), a later change to the LEADER's tempo pulls the synced follower's audible
/// rate (ReapplySyncedFollowers) but the handler re-emits DeckBpm feedback ONLY for the moved leader
/// slot — never for the synced follower. So the follower's counter freezes at its engage-time value
/// while its audible tempo tracks the leader: the two counters diverge and the follower's counter
/// lies about what is playing.
///
/// Wires the REAL engine + REAL handler through the fake BASS backend (no native audio).
/// </summary>
public class SyncedFollowerBpmCounterReproTests
{
    private static TwoDeckBassEngine NewPair(out FakeBassMixerBackend backend, double leaderBpm, double followerBpm)
    {
        backend = new FakeBassMixerBackend();
        var engine = new TwoDeckBassEngine(backend, new BassMixer(deckCount: TwoDeckBassEngine.Decks));
        engine.Load(0, @"C:\leader.wav");   // slot 0 = sync leader
        engine.Load(1, @"C:\follower.wav"); // slot 1 = synced follower
        engine.SetDeckBaseBpm(0, leaderBpm);
        engine.SetDeckBaseBpm(1, followerBpm);
        return engine;
    }

    [Fact]
    public void Repro_SyncedFollowerBpmCounter_FreezesWhenLeaderTempoMoves()
    {
        // Leader 128, follower 130 — close tempos so the beatmatch stays comfortably in the ±15% sync range
        // through the whole test (no octave fold, no OutOfRange), isolating the feedback defect.
        using TwoDeckBassEngine engine = NewPair(out _, leaderBpm: 128.0, followerBpm: 130.0);
        var handler = new DeckActionHandler(engine);

        // The on-screen counter mirrors the last DeckBpm feedback value per slot.
        var counter = new double[engine.DeckCount];
        handler.FeedbackChanged += (_, e) =>
        {
            if (e.Kind == PerformanceActionKind.DeckBpm)
                counter[e.Slot] = e.State.Value;
        };
        // Seed both counters as the UI does on entry (GetFeedback -> ApplyBpmFeedback).
        counter[0] = handler.GetFeedback(PerformanceActionKind.DeckBpm, 0).Value;
        counter[1] = handler.GetFeedback(PerformanceActionKind.DeckBpm, 1).Value;

        // Engage continuous Sync Lock on the follower (the DeckSyncToggle a controller maps).
        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckSyncToggle, ActionInputMode.Momentary, Slot: 1));

        // Sanity: at engage both counters read the leader's tempo — they match (this part works).
        Assert.Equal(128.0, engine.DeckBpm(1), 3); // follower beatmatched to leader audibly
        Assert.Equal(128.0, counter[1], 3);         // and its counter was refreshed at engage

        // Now the DJ nudges the LEADER's pitch fader up to +4% (pos 0.75 -> rate 1.04).
        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckPitch, ActionInputMode.Absolute, Value: 0.75, Slot: 0));

        double leaderAudible = engine.DeckBpm(0);   // 128 * 1.04 = 133.12
        double followerAudible = engine.DeckBpm(1); // synced: tracks the leader -> 133.12

        // The engine did the right thing: the synced follower's AUDIBLE tempo followed the leader.
        Assert.Equal(leaderAudible, followerAudible, 3);
        Assert.True(followerAudible > 130.0, $"follower audible should track leader up, was {followerAudible}");

        // The leader's counter refreshed...
        Assert.Equal(leaderAudible, counter[0], 3);

        // ...but the follower's counter is FROZEN at its engage value (128), so it now lies about the
        // audible tempo. This assertion is the defect: the on-screen counter must equal what is playing.
        Assert.Equal(followerAudible, counter[1], 3);
    }
}
