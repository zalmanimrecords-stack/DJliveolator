using Liveolator.Audio.Playback;
using Liveolator.Core.Audio.Sync;
using Liveolator.Core.Mixer;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

public class BeatSyncMediaTimeTests
{
    [Theory]
    [InlineData(132, 120, 1.0, 0.0)]
    [InlineData(120, 132, 1.0, 0.0)]
    [InlineData(128, 124, 1.04, 0.08)]
    [InlineData(132, 60, 1.0, 0.08)]
    public void AlignedMix_HoldsMatchedRateForFiveMinutes(
        double masterBpm, double followerBpm, double masterRate, double latency)
    {
        var backend = new FakeBassMixerBackend();
        using var engine = new TwoDeckBassEngine(backend, new BassMixer(2),
            phaseLock: new PhaseLockSettings(OutputLatencySeconds: latency));
        engine.Load(0, @"C:\master.wav");
        engine.Load(1, @"C:\follower.wav");
        backend.LengthSeconds[100] = backend.LengthSeconds[101] = 1000;
        engine.SetDeckBaseBpm(0, masterBpm);
        engine.SetDeckBaseBpm(1, followerBpm);
        engine.SetDeckBpm(0, masterBpm * masterRate);
        engine.SetDeckPhaseSyncReady(0, true);
        engine.SetDeckPhaseSyncReady(1, true);
        engine.PlayPause(0);
        engine.SetSyncLock(1, true);
        engine.PlayPause(1);
        double matchedRate = TempoSyncCalculator.RateFor(masterBpm * masterRate, followerBpm);
        // Integrate source playheads using the actual rate requested by the controller.
        for (int tick = 0; tick < 18750; tick++)
        {
            backend.PositionFraction[100] = backend.GetDeckPositionFraction(100) + masterRate * .016 / 1000;
            backend.PositionFraction[101] = backend.GetDeckPositionFraction(101) + backend.Rate[101] * .016 / 1000;
            double before = backend.GetDeckPositionFraction(101);
            engine.UpdateSync(tick);
            Assert.Equal(matchedRate, backend.Rate[101], 9);
            Assert.Equal(before, backend.GetDeckPositionFraction(101));
            Assert.Equal(SyncLockState.Locked, engine.SyncState(1));
        }
    }
    [Theory]
    [InlineData(132, 120, 1.0, false, false)]
    [InlineData(120, 132, 1.0, false, false)]
    [InlineData(128, 124, 1.04, false, true)]
    [InlineData(132, 120, 1.0, true, false)]
    [InlineData(120, 132, 1.0, true, false)]
    [InlineData(128, 124, 1.04, true, false)]
    public void PhaseSnap_AlignsPhysicalGridAtDifferentPlaybackRates(
        double masterBpm, double followerBpm, double masterRate, bool continuous, bool bar)
    {
        var backend = new FakeBassMixerBackend();
        using var engine = new TwoDeckBassEngine(backend, new BassMixer(2),
            phaseLock: new PhaseLockSettings(OutputLatencySeconds: .08));
        engine.Load(0, @"C:\master.wav");
        engine.Load(1, @"C:\follower.wav");
        engine.SetDeckBaseBpm(0, masterBpm);
        engine.SetDeckBaseBpm(1, followerBpm);
        engine.SetDeckBpm(0, masterBpm * masterRate);
        engine.SetDeckFirstBeat(0, .1);
        engine.SetDeckFirstBeat(1, .3);
        engine.SetDeckPhaseSyncReady(0, true);
        engine.SetDeckPhaseSyncReady(1, true);
        engine.PlayPause(0);
        if (bar)
        {
            engine.SetDeckDownbeat(0, .1);
            engine.SetDeckDownbeat(1, .3);
        }
        if (continuous)
        {
            engine.SetSyncLock(1, true);
            engine.PlayPause(1);
        }
        backend.PositionFraction[100] = (.1 + 20.4 * 60 / masterBpm) / 100;
        backend.PositionFraction[101] = (.3 + 20.05 * 60 / followerBpm) / 100;
        if (continuous)
            engine.UpdateSync(1);
        else
            engine.SyncOnce(1);

        // Independent oracle: compare source-grid beat counts, rather than asking the
        // same phase calculator used by the implementation to validate its own answer.
        double masterBeats = (backend.GetDeckPositionSeconds(100) - .1) * masterBpm / 60;
        double followerBeats = (backend.GetDeckPositionSeconds(101) - .3) * followerBpm / 60;
        double gridError = (masterBeats - followerBeats) / (bar ? 4 : 1);
        Assert.Equal(0, gridError - Math.Round(gridError), 9);
    }
}
