using Liveolator.Core.Beat;
using Xunit;

namespace Liveolator.Core.Tests.Beat;

/// <summary>
/// The clock-driven beat scheduler (doc 31 feature): quantized launches fire on the shared clock's
/// beat/bar boundaries, falling back to immediate when the grid is not yet trustworthy.
/// </summary>
public class ClockBeatSchedulerTests
{
    private static BeatClockState Confident(bool isBeat = false, bool isDownbeat = false, int barNumber = 1)
        => new(
            Bpm: 128, Confidence: 0.9, BeatPhase: 0, BarPhase: 0,
            BeatCount: 0, BarNumber: barNumber, IsBeat: isBeat, IsDownbeat: isDownbeat,
            IsLocked: true, Source: BeatClockSource.Deck, Candidates: Array.Empty<TempoCandidate>());

    private sealed class FakeBeatClock : IBeatClock
    {
        public BeatClockState Current { get; set; } = BeatClockState.Idle;
        public event EventHandler<BeatClockState>? StateChanged;
        public void Publish(BeatClockState state)
        {
            Current = state;
            StateChanged?.Invoke(this, state);
        }
    }

    [Fact]
    public void Immediate_FiresNow()
    {
        var clock = new FakeBeatClock { Current = Confident() };
        using var scheduler = new ClockBeatScheduler(clock);
        int fired = 0;

        scheduler.Schedule(Quantize.Immediate, 1, () => fired++);

        Assert.Equal(1, fired);
    }

    [Fact]
    public void NextBeat_DefersUntilTheNextBeatBoundary_ThenFiresOnce()
    {
        var clock = new FakeBeatClock { Current = Confident() };
        using var scheduler = new ClockBeatScheduler(clock);
        int fired = 0;

        scheduler.Schedule(Quantize.NextBeat, 1, () => fired++);
        Assert.Equal(0, fired);                 // not fired at schedule time

        clock.Publish(Confident(isBeat: false));
        Assert.Equal(0, fired);                 // a non-boundary tick does not fire it

        clock.Publish(Confident(isBeat: true));
        Assert.Equal(1, fired);                 // fires on the beat boundary

        clock.Publish(Confident(isBeat: true));
        Assert.Equal(1, fired);                 // removed after firing — does not re-fire
    }

    [Fact]
    public void NextBar_FiresOnDownbeat_NotOnAPlainBeat()
    {
        var clock = new FakeBeatClock { Current = Confident() };
        using var scheduler = new ClockBeatScheduler(clock);
        int fired = 0;

        scheduler.Schedule(Quantize.NextBar, 1, () => fired++);

        clock.Publish(Confident(isBeat: true, isDownbeat: false));
        Assert.Equal(0, fired);                 // a beat that is not a downbeat does not fire it

        clock.Publish(Confident(isBeat: true, isDownbeat: true));
        Assert.Equal(1, fired);
    }

    [Fact]
    public void LowConfidence_FiresImmediately_RatherThanSnappingToAShakyGrid()
    {
        var clock = new FakeBeatClock
        {
            Current = Confident() with { Confidence = 0.1 },
        };
        using var scheduler = new ClockBeatScheduler(clock);
        int fired = 0;

        scheduler.Schedule(Quantize.NextBar, 1, () => fired++);

        Assert.Equal(1, fired);
    }

    [Fact]
    public void NoTempoYet_FiresImmediately()
    {
        var clock = new FakeBeatClock { Current = BeatClockState.Idle }; // Bpm 0
        using var scheduler = new ClockBeatScheduler(clock);
        int fired = 0;

        scheduler.Schedule(Quantize.NextBeat, 1, () => fired++);

        Assert.Equal(1, fired);
    }

    [Fact]
    public void EveryNBars_FiresOnlyOnAMatchingBarBoundary()
    {
        var clock = new FakeBeatClock { Current = Confident() };
        using var scheduler = new ClockBeatScheduler(clock);
        int fired = 0;

        scheduler.Schedule(Quantize.EveryNBars, 4, () => fired++);

        clock.Publish(Confident(isDownbeat: true, barNumber: 3)); // 3 % 4 != 0
        Assert.Equal(0, fired);
        clock.Publish(Confident(isDownbeat: true, barNumber: 4)); // 4 % 4 == 0
        Assert.Equal(1, fired);
    }

    [Fact]
    public void MultiplePending_FireIndependentlyOnTheirOwnBoundaries()
    {
        var clock = new FakeBeatClock { Current = Confident() };
        using var scheduler = new ClockBeatScheduler(clock);
        int beat = 0, bar = 0;

        scheduler.Schedule(Quantize.NextBeat, 1, () => beat++);
        scheduler.Schedule(Quantize.NextBar, 1, () => bar++);

        clock.Publish(Confident(isBeat: true, isDownbeat: false));
        Assert.Equal(1, beat);
        Assert.Equal(0, bar);

        clock.Publish(Confident(isBeat: true, isDownbeat: true));
        Assert.Equal(1, beat); // already fired, not again
        Assert.Equal(1, bar);
    }
}
