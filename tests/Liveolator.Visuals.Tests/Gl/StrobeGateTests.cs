using Liveolator.Core.Beat;
using Liveolator.Visuals.Gl;

namespace Liveolator.Visuals.Tests.Gl;

public sealed class StrobeGateTests
{
    private static BeatClockState Beat(double confidence = 1.0, double beatPhase = 0.0)
        => BeatClockState.Idle with
        {
            Bpm = 120,
            Confidence = confidence,
            BeatPhase = beatPhase,
        };

    [Fact]
    public void Off_when_strobe_is_not_engaged()
    {
        // Even a fully-confident on-beat clock contributes nothing when the latch is off.
        double gate = StrobeGate.Resolve(Beat(confidence: 1.0, beatPhase: 0.0), strobeOn: false);

        Assert.Equal(1.0, gate, 6); // no attenuation -> full pass-through
    }

    [Fact]
    public void On_phase_passes_full_brightness()
    {
        // At the top of the beat the strobe is in its ON window.
        double gate = StrobeGate.Resolve(Beat(beatPhase: 0.0), strobeOn: true, onFraction: 0.5);

        Assert.Equal(1.0, gate, 6);
    }

    [Fact]
    public void Off_phase_blacks_out()
    {
        // Past the ON window the strobe gate closes (black) for the rest of the beat.
        double gate = StrobeGate.Resolve(Beat(beatPhase: 0.75), strobeOn: true, onFraction: 0.5);

        Assert.Equal(0.0, gate, 6);
    }

    [Fact]
    public void On_window_boundary_is_exclusive()
    {
        // The ON window is [0, onFraction); exactly at the boundary the gate is already closed.
        double gate = StrobeGate.Resolve(Beat(beatPhase: 0.5), strobeOn: true, onFraction: 0.5);

        Assert.Equal(0.0, gate, 6);
    }

    [Fact]
    public void Low_confidence_does_not_strobe()
    {
        // A shaky clock must not flicker the whole output (mirrors the beat-flash confidence gate).
        double gate = StrobeGate.Resolve(
            Beat(confidence: 0.1, beatPhase: 0.75), strobeOn: true, onFraction: 0.5, minConfidence: 0.5);

        Assert.Equal(1.0, gate, 6); // gate stays open -> no strobing on an untrustworthy grid
    }

    [Fact]
    public void Confidence_at_threshold_strobes()
    {
        double gate = StrobeGate.Resolve(
            Beat(confidence: 0.5, beatPhase: 0.75), strobeOn: true, onFraction: 0.5, minConfidence: 0.5);

        Assert.Equal(0.0, gate, 6);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void On_fraction_out_of_range_is_clamped(double onFraction)
    {
        // A degenerate onFraction must not throw; it clamps into (0,1] so the strobe stays well-defined.
        double gate = StrobeGate.Resolve(Beat(beatPhase: 0.999), strobeOn: true, onFraction: onFraction);

        Assert.InRange(gate, 0.0, 1.0);
    }

    [Fact]
    public void Rejects_a_null_beat_state()
        => Assert.Throws<ArgumentNullException>(() => StrobeGate.Resolve(beat: null!, strobeOn: true));
}
