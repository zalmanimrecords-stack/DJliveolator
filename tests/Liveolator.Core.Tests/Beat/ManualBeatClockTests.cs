using Liveolator.Core.Beat;
using Xunit;

namespace Liveolator.Core.Tests.Beat;

public class ManualBeatClockTests
{
    private const long Ms = 1000;

    private static ManualBeatClock ClockAt120()
    {
        var clock = new ManualBeatClock(Ms);
        clock.Tap(0);
        clock.Tap(500); // 500 ms interval → 120 BPM, grid anchored at the last tap
        return clock;
    }

    [Fact]
    public void TwoTaps_EstablishTempoAndFireABeat()
    {
        ManualBeatClock clock = ClockAt120();

        Assert.Equal(120, clock.Bpm, precision: 6);
        Assert.Equal(BeatClockSource.Manual, clock.Current.Source);
        Assert.True(clock.Current.IsBeat);
        Assert.Equal(1.0, clock.Current.Confidence, precision: 6);
    }

    [Fact]
    public void Update_AdvancesPhaseWithinABeat()
    {
        ManualBeatClock clock = ClockAt120();

        clock.Update(500 + 250); // 250 ms past the anchor = half a beat at 120 BPM

        Assert.Equal(0.5, clock.Current.BeatPhase, precision: 6);
        Assert.False(clock.Current.IsBeat);
    }

    [Fact]
    public void Update_CrossingABeatBoundary_FiresIsBeat()
    {
        ManualBeatClock clock = ClockAt120();

        clock.Update(500 + 250);  // mid-beat, no crossing
        clock.Update(500 + 500);  // next beat boundary

        Assert.True(clock.Current.IsBeat);
        Assert.Equal(1, clock.Current.BeatCount);
    }

    [Fact]
    public void Update_CrossingABarBoundary_FiresDownbeat()
    {
        ManualBeatClock clock = ClockAt120();

        clock.Update(500 + 2000); // 4 beats past the anchor → bar 1

        Assert.True(clock.Current.IsDownbeat);
        Assert.Equal(1, clock.Current.BarNumber);
    }

    [Fact]
    public void HalfTempo_HalvesBpm()
    {
        ManualBeatClock clock = ClockAt120();
        clock.HalfTempo(500);
        Assert.Equal(60, clock.Bpm, precision: 6);
    }

    [Fact]
    public void DoubleTempo_DoublesBpm()
    {
        ManualBeatClock clock = ClockAt120();
        clock.DoubleTempo(500);
        Assert.Equal(240, clock.Bpm, precision: 6);
    }

    [Fact]
    public void Lock_PreventsTapsFromChangingTempo()
    {
        ManualBeatClock clock = ClockAt120();
        clock.Lock();

        // Taps now imply a faster tempo, but the frozen BPM must hold.
        clock.Tap(1000);
        clock.Tap(1300);

        Assert.True(clock.IsLocked);
        Assert.Equal(120, clock.Bpm, precision: 6);
    }

    [Fact]
    public void HalfTempo_AppliesEvenWhenLocked()
    {
        ManualBeatClock clock = ClockAt120();
        clock.Lock();

        clock.HalfTempo(500); // explicit performer command bypasses the freeze

        Assert.Equal(60, clock.Bpm, precision: 6);
    }

    [Fact]
    public void Nudge_ShiftsThePhase()
    {
        ManualBeatClock clock = ClockAt120();
        clock.Update(500 + 250); // phase 0.5

        clock.Nudge(0.25, 500 + 250);

        Assert.Equal(0.75, clock.Current.BeatPhase, precision: 6);
    }

    [Fact]
    public void SetDownbeat_ReanchorsGridToNow()
    {
        ManualBeatClock clock = ClockAt120();
        clock.Update(500 + 1234);

        clock.SetDownbeat(500 + 1234);

        Assert.Equal(0, clock.Current.BeatCount);
        Assert.Equal(0.0, clock.Current.BeatPhase, precision: 6);
        Assert.True(clock.Current.IsDownbeat);
    }

    [Fact]
    public void StateChanged_FiresOnEveryPublish()
    {
        var clock = new ManualBeatClock(Ms);
        int count = 0;
        clock.StateChanged += (_, _) => count++;

        clock.Tap(0);   // no tempo yet → no publish
        clock.Tap(500); // tempo established → publish

        Assert.True(count >= 1);
    }

    [Fact]
    public void Constructor_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManualBeatClock(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManualBeatClock(Ms, beatsPerBar: 0));
    }
}
