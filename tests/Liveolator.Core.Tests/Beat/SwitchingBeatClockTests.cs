using Liveolator.Core.Beat;
using Xunit;

namespace Liveolator.Core.Tests.Beat;

public class SwitchingBeatClockTests
{
    [Fact]
    public void ForwardsStateChangesFromTheActiveSource()
    {
        var a = new FakeClock();
        var b = new FakeClock();
        var clock = new SwitchingBeatClock(a);
        BeatClockState? seen = null;
        clock.StateChanged += (_, s) => seen = s;

        a.Publish(120.0);

        Assert.Equal(120.0, seen!.Bpm, precision: 6);
    }

    [Fact]
    public void DoesNotForwardFromAnInactiveSource()
    {
        var a = new FakeClock();
        var b = new FakeClock();
        var clock = new SwitchingBeatClock(a);
        int events = 0;
        clock.StateChanged += (_, _) => events++;

        b.Publish(128.0); // b is not active

        Assert.Equal(0, events);
    }

    [Fact]
    public void Select_SwitchesSource_RepublishesAndForwardsNewSource()
    {
        var a = new FakeClock();
        var b = new FakeClock();
        b.Publish(130.0); // b already has a current state
        var clock = new SwitchingBeatClock(a);
        var seen = new List<double>();
        clock.StateChanged += (_, s) => seen.Add(s.Bpm);

        clock.Select(b);   // republishes b.Current immediately...
        b.Publish(131.0);  // ...and now forwards b's changes
        a.Publish(120.0);  // a is no longer active

        Assert.Equal(b, clock.Active);
        Assert.Equal(new[] { 130.0, 131.0 }, seen);
    }

    [Fact]
    public void Select_SameSource_IsNoOp()
    {
        var a = new FakeClock();
        var clock = new SwitchingBeatClock(a);
        int events = 0;
        clock.StateChanged += (_, _) => events++;

        clock.Select(a);

        Assert.Equal(0, events);
    }

    private sealed class FakeClock : IBeatClock
    {
        public BeatClockState Current { get; private set; } = BeatClockState.Idle;
        public event EventHandler<BeatClockState>? StateChanged;

        public void Publish(double bpm)
        {
            Current = BeatClockState.Idle with { Bpm = bpm };
            StateChanged?.Invoke(this, Current);
        }
    }
}
