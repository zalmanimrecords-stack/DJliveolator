using Liveolator.Core.Audio;
using Liveolator.Core.Beat;
using Xunit;

namespace Liveolator.Core.Tests.Beat;

public class MasterClockBridgeTests
{
    private const long Ticks = TimeSpan.TicksPerSecond;

    [Fact]
    public void Tick_PumpsTheCorrectionLoop()
    {
        var sync = new FakeSyncDriver();
        var bridge = NewBridge(sync, out _, out _);

        bridge.Tick(hostTimeTicks: 42);

        Assert.Equal(42, sync.LastUpdateTicks);
    }

    [Fact]
    public void Tick_WithMaster_PointsSharedClockAtTheDeckClock()
    {
        var sync = new FakeSyncDriver { MasterBpm = 128.0, MasterBeat = 4.0 };
        var bridge = NewBridge(sync, out var deckClock, out var shared);

        bridge.Tick(0);

        Assert.Same(deckClock, shared.Active);
        Assert.Equal(128.0, shared.Current.Bpm, precision: 6);
        Assert.Equal(BeatClockSource.Deck, shared.Current.Source);
    }

    [Fact]
    public void Tick_WithoutMaster_FallsBackToTheBaseClock()
    {
        var sync = new FakeSyncDriver { HasMaster = false };
        var bridge = NewBridge(sync, out _, out var shared, out var baseClock);

        bridge.Tick(0);

        Assert.Same(baseClock, shared.Active);
    }

    [Fact]
    public void Tick_MasterThenStops_SwitchesDeckClockThenBackToBase()
    {
        var sync = new FakeSyncDriver { MasterBpm = 124.0, MasterBeat = 1.0 };
        var bridge = NewBridge(sync, out var deckClock, out var shared, out var baseClock);

        bridge.Tick(0);
        Assert.Same(deckClock, shared.Active);

        sync.HasMaster = false; // master released
        bridge.Tick(Ticks);

        Assert.Same(baseClock, shared.Active);
    }

    private static MasterClockBridge NewBridge(
        FakeSyncDriver sync, out DeckDrivenBeatClock deckClock, out SwitchingBeatClock shared)
        => NewBridge(sync, out deckClock, out shared, out _);

    private static MasterClockBridge NewBridge(
        FakeSyncDriver sync, out DeckDrivenBeatClock deckClock, out SwitchingBeatClock shared,
        out IBeatClock baseClock)
    {
        deckClock = new DeckDrivenBeatClock(Ticks);
        var baseManual = new ManualBeatClock(Ticks);
        baseClock = baseManual;
        shared = new SwitchingBeatClock(baseManual);
        return new MasterClockBridge(sync, deckClock, shared, baseManual);
    }

    private sealed class FakeSyncDriver : ISyncCorrectionDriver
    {
        public long? LastUpdateTicks { get; private set; }
        public bool HasMaster { get; set; } = true;
        public double MasterBpm { get; set; } = 120.0;
        public double MasterBeat { get; set; }

        public void UpdateSync(long hostTimeTicks) => LastUpdateTicks = hostTimeTicks;

        public bool TryGetSyncMasterBeat(out double effectiveBpm, out double continuousBeat)
        {
            effectiveBpm = MasterBpm;
            continuousBeat = MasterBeat;
            return HasMaster;
        }
    }
}
