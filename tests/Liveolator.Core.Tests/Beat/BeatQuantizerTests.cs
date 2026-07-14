using Liveolator.Core.Beat;
using Xunit;

namespace Liveolator.Core.Tests.Beat;

public class BeatQuantizerTests
{
    // 120 BPM at 1000 ticks/sec → one beat = 500 ms, one 4/4 bar = 2000 ms.
    private static readonly BeatTimeline Timeline = new(bpm: 120, anchorBeat: 0, anchorHostTimeTicks: 0, ticksPerSecond: 1000);

    [Fact]
    public void Immediate_FiresAtTheGivenTime()
        => Assert.Equal(317, BeatQuantizer.ResolveFireTime(Quantize.Immediate, 0, 317, Timeline));

    [Fact]
    public void NextBeat_SnapsToTheNextBeat()
        => Assert.Equal(500, BeatQuantizer.ResolveFireTime(Quantize.NextBeat, 0, 250, Timeline));

    [Fact]
    public void NextBar_SnapsToTheNextBar()
        => Assert.Equal(2000, BeatQuantizer.ResolveFireTime(Quantize.NextBar, 0, 600, Timeline));

    [Fact]
    public void EveryNBars_SnapsToTheNextNBarBoundary()
    {
        // every 2 bars = 8 beats = 4000 ms; from 100 ms (beat 0.2) the next boundary is beat 8.
        Assert.Equal(4000, BeatQuantizer.ResolveFireTime(Quantize.EveryNBars, everyN: 2, 100, Timeline));
    }

    [Fact]
    public void NextBar_HonorsCustomBeatsPerBar()
    {
        // 3/4: one bar = 3 beats = 1500 ms; from 100 ms the next bar boundary is beat 3.
        Assert.Equal(1500, BeatQuantizer.ResolveFireTime(Quantize.NextBar, 0, 100, Timeline, beatsPerBar: 3));
    }

    [Fact]
    public void EveryNBars_RejectsNonPositiveN()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => BeatQuantizer.ResolveFireTime(Quantize.EveryNBars, everyN: 0, 100, Timeline));

    [Fact]
    public void ResolveFireTime_RejectsNullTimeline()
        => Assert.Throws<ArgumentNullException>(
            () => BeatQuantizer.ResolveFireTime(Quantize.NextBeat, 0, 0, null!));

    [Fact]
    public void ResolveFireTime_RejectsNonPositiveBeatsPerBar()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => BeatQuantizer.ResolveFireTime(Quantize.NextBar, 0, 0, Timeline, beatsPerBar: 0));
}
