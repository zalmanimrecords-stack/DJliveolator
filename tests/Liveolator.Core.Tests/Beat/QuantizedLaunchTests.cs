using Liveolator.Core.Beat;
using Xunit;

namespace Liveolator.Core.Tests.Beat;

public class QuantizedLaunchTests
{
    // 120 BPM at 1000 ticks/sec → one beat = 500 ms.
    private static readonly BeatTimeline Timeline = new(bpm: 120, anchorBeat: 0, anchorHostTimeTicks: 0, ticksPerSecond: 1000);

    [Fact]
    public void Immediate_AlwaysFiresNow()
        => Assert.Equal(250, QuantizedLaunch.ResolveFireTime(Quantize.Immediate, 1, 250, Timeline, confidence: 1.0));

    [Fact]
    public void HighConfidence_SnapsToBoundary()
        => Assert.Equal(500, QuantizedLaunch.ResolveFireTime(Quantize.NextBeat, 1, 250, Timeline, confidence: 1.0));

    [Fact]
    public void LowConfidence_FallsBackToImmediate()
        => Assert.Equal(250, QuantizedLaunch.ResolveFireTime(Quantize.NextBeat, 1, 250, Timeline, confidence: 0.1));

    [Fact]
    public void NullTimeline_FallsBackToImmediate()
        => Assert.Equal(250, QuantizedLaunch.ResolveFireTime(Quantize.NextBar, 1, 250, timeline: null, confidence: 1.0));

    [Fact]
    public void ConfidenceExactlyAtThreshold_IsHonored()
        => Assert.Equal(500, QuantizedLaunch.ResolveFireTime(
            Quantize.NextBeat, 1, 250, Timeline, confidence: QuantizedLaunch.DefaultMinConfidence));
}
