using Liveolator.Core.Beat;
using Xunit;

namespace Liveolator.Core.Tests.Beat;

/// <summary>
/// The master-deck-driven shared clock: pure, time-supplied, so beat/bar phase and crossings assert
/// deterministically against a known tempo and position.
/// </summary>
public class DeckDrivenBeatClockTests
{
    // Any tick resolution works since the clock re-anchors at the supplied host time and reads at the
    // same time; the published beat equals the supplied continuous beat regardless of the tick value.
    private const long Ticks = TimeSpan.TicksPerSecond;

    private static DeckDrivenBeatClock NewClock() => new(Ticks);

    [Fact]
    public void Update_PublishesDeckSourcedStateAtSuppliedTempo()
    {
        var clock = NewClock();

        clock.Update(effectiveBpm: 128.0, continuousBeat: 0.0, hostTimeTicks: 0);

        Assert.Equal(BeatClockSource.Deck, clock.Current.Source);
        Assert.Equal(128.0, clock.Current.Bpm, precision: 6);
        Assert.Equal(0.0, clock.Current.BeatPhase, precision: 6);
        Assert.Equal(1.0, clock.Current.Confidence, precision: 6);
        Assert.True(clock.Current.IsBeat); // first beat crossing
    }

    [Fact]
    public void Update_ReportsBeatPhaseFromContinuousBeat()
    {
        var clock = NewClock();

        clock.Update(effectiveBpm: 120.0, continuousBeat: 2.5, hostTimeTicks: 0);

        Assert.Equal(0.5, clock.Current.BeatPhase, precision: 6);
        Assert.Equal(2, clock.Current.BeatCount);
    }

    [Fact]
    public void Update_OnBarBoundary_FlagsDownbeat()
    {
        var clock = NewClock();

        clock.Update(120.0, continuousBeat: 4.0, hostTimeTicks: 0); // beat 4 = bar 1 downbeat (4/4)

        Assert.Equal(0.0, clock.Current.BarPhase, precision: 6);
        Assert.Equal(1, clock.Current.BarNumber);
        Assert.True(clock.Current.IsDownbeat);
    }

    [Fact]
    public void Update_RaisesStateChanged()
    {
        var clock = NewClock();
        BeatClockState? seen = null;
        clock.StateChanged += (_, s) => seen = s;

        clock.Update(124.0, 1.0, 0);

        Assert.NotNull(seen);
        Assert.Equal(124.0, seen!.Bpm, precision: 6);
    }

    [Fact]
    public void Update_TempoChange_StaysContinuousAndTracksNewBpm()
    {
        var clock = NewClock();

        clock.Update(120.0, continuousBeat: 8.0, hostTimeTicks: 0);
        clock.Update(140.0, continuousBeat: 8.25, hostTimeTicks: Ticks / 8); // re-anchored on the true beat

        Assert.Equal(140.0, clock.Current.Bpm, precision: 6);
        Assert.Equal(0.25, clock.Current.BeatPhase, precision: 6);
        Assert.Equal(8, clock.Current.BeatCount);
    }

    [Fact]
    public void Update_NonPositiveTempo_GoesIdle()
    {
        var clock = NewClock();
        clock.Update(120.0, 1.0, 0);

        clock.Update(effectiveBpm: 0.0, continuousBeat: 0.0, hostTimeTicks: Ticks);

        Assert.Equal(0.0, clock.Current.Bpm, precision: 6);
        Assert.Equal(BeatClockState.Idle, clock.Current);
    }

    [Fact]
    public void Reset_AfterPlaying_PublishesIdleOnce()
    {
        var clock = NewClock();
        clock.Update(120.0, 1.0, 0);

        int idleEvents = 0;
        clock.StateChanged += (_, s) => { if (s == BeatClockState.Idle) idleEvents++; };
        clock.Reset();
        clock.Reset(); // already idle — must not re-publish

        Assert.Equal(1, idleEvents);
    }
}
