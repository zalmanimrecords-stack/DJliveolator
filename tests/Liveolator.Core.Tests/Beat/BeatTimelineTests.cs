using Liveolator.Core.Beat;
using Xunit;

namespace Liveolator.Core.Tests.Beat;

public class BeatTimelineTests
{
    // 120 BPM at 1000 ticks/sec (ms) → 2 beats/sec → one beat = 500 ms.
    private static BeatTimeline AtAnchorZero(double bpm = 120)
        => new(bpm, anchorBeat: 0, anchorHostTimeTicks: 0, ticksPerSecond: 1000);

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(250, 0.5)]
    [InlineData(500, 1.0)]
    [InlineData(1000, 2.0)]
    [InlineData(-500, -1.0)]
    public void BeatAtTime_MapsHostTimeToBeat(long ticks, double expectedBeat)
        => Assert.Equal(expectedBeat, AtAnchorZero().BeatAtTime(ticks), precision: 9);

    [Theory]
    [InlineData(250, 1, 0.5)]
    [InlineData(500, 1, 0.0)]   // on the beat → phase 0
    [InlineData(750, 4, 0.375)] // beat 1.5 of a 4-beat grid
    [InlineData(-250, 1, 0.5)]  // before the anchor still wraps into [0,1)
    public void PhaseAtTime_WrapsIntoQuantum(long ticks, double quantum, double expectedPhase)
        => Assert.Equal(expectedPhase, AtAnchorZero().PhaseAtTime(ticks, quantum), precision: 9);

    [Theory]
    [InlineData(250, 1, 500)]    // mid-beat → next beat
    [InlineData(500, 1, 500)]    // exactly on a beat → "now"
    [InlineData(0, 1, 0)]        // exactly on the anchor → "now"
    [InlineData(600, 4, 2000)]   // beat 1.2 → next bar boundary (beat 4)
    public void NextBoundary_SnapsToAtOrAfter(long fromTicks, double quantum, long expectedTicks)
        => Assert.Equal(expectedTicks, AtAnchorZero().NextBoundary(fromTicks, quantum));

    [Fact]
    public void Anchor_OffsetIsHonored()
    {
        var timeline = new BeatTimeline(bpm: 120, anchorBeat: 2, anchorHostTimeTicks: 1000, ticksPerSecond: 1000);

        Assert.Equal(2.0, timeline.BeatAtTime(1000), precision: 9);
        Assert.Equal(3.0, timeline.BeatAtTime(1500), precision: 9);
        Assert.Equal(2000, timeline.NextBoundary(1000, 4)); // beat 2 → next 4-beat boundary at beat 4
    }

    [Fact]
    public void FromSystemClock_UsesDotNetTickResolution()
    {
        var timeline = BeatTimeline.FromSystemClock(120, anchorBeat: 0, anchorHostTimeTicks: 0);
        Assert.Equal(TimeSpan.TicksPerSecond, timeline.TicksPerSecond);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-120)]
    public void Constructor_RejectsNonPositiveBpm(double bpm)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new BeatTimeline(bpm, 0, 0, 1000));

    [Fact]
    public void Constructor_RejectsNonPositiveTickRate()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new BeatTimeline(120, 0, 0, 0));

    [Fact]
    public void PhaseAtTime_RejectsNonPositiveQuantum()
        => Assert.Throws<ArgumentOutOfRangeException>(() => AtAnchorZero().PhaseAtTime(0, 0));

    [Fact]
    public void NextBoundary_RejectsNonPositiveQuantum()
        => Assert.Throws<ArgumentOutOfRangeException>(() => AtAnchorZero().NextBoundary(0, -1));
}
