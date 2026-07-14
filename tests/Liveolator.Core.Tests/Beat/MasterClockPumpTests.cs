using Liveolator.Core.Audio;
using Liveolator.Core.Beat;
using Xunit;

namespace Liveolator.Core.Tests.Beat;

public sealed class MasterClockPumpTests
{
    [Fact]
    public void Start_PumpsTheBridgeOffTheCallingThread()
    {
        int callingThread = Environment.CurrentManagedThreadId;
        var sync = new RecordingSyncDriver();
        var hostClock = new SystemHostClock();
        var baseClock = new ManualBeatClock(hostClock.TicksPerSecond);
        var bridge = new MasterClockBridge(
            sync,
            new DeckDrivenBeatClock(hostClock.TicksPerSecond),
            new SwitchingBeatClock(baseClock),
            baseClock);

        using var pump = new MasterClockPump(
            bridge, hostClock, interval: TimeSpan.FromMilliseconds(1));
        pump.Start();

        Assert.True(sync.Pumped.Wait(TimeSpan.FromSeconds(2)));
        Assert.NotEqual(callingThread, sync.LastThreadId);
    }

    private sealed class RecordingSyncDriver : ISyncCorrectionDriver
    {
        public ManualResetEventSlim Pumped { get; } = new(false);
        public int LastThreadId { get; private set; }

        public void UpdateSync(long hostTimeTicks)
        {
            LastThreadId = Environment.CurrentManagedThreadId;
            Pumped.Set();
        }

        public bool TryGetSyncMasterBeat(out double effectiveBpm, out double continuousBeat)
        {
            effectiveBpm = 0.0;
            continuousBeat = 0.0;
            return false;
        }
    }
}
