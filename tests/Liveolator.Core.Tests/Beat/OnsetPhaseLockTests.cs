using Liveolator.Core.Beat;
using Xunit;

namespace Liveolator.Core.Tests.Beat;

public sealed class OnsetPhaseLockTests
{
    [Fact]
    public void Correct_OnsetOnTheBeat_AppliesNoCorrection()
    {
        var pll = new OnsetPhaseLock();
        PhaseCorrection result = pll.Correct(beatPhaseAtOnset: 0.0);

        Assert.False(result.Applied);
        Assert.Equal(0.0, result.BeatAdjustment);
    }

    [Fact]
    public void Correct_OnsetSlightlyLate_NudgesGridForward()
    {
        // Onset 0.1 beats after the beat → pull the grid toward it (positive adjustment).
        var pll = new OnsetPhaseLock(gain: 0.5, maxBeatStep: 0.5);
        PhaseCorrection result = pll.Correct(beatPhaseAtOnset: 0.1);

        Assert.True(result.Applied);
        Assert.Equal(0.05, result.BeatAdjustment, 6); // gain 0.5 × error 0.1
    }

    [Fact]
    public void Correct_OnsetSlightlyEarly_NudgesGridBackward()
    {
        // Onset at phase 0.9 = 0.1 beats *before* the next beat → negative (signed) error.
        var pll = new OnsetPhaseLock(gain: 0.5, maxBeatStep: 0.5);
        PhaseCorrection result = pll.Correct(beatPhaseAtOnset: 0.9);

        Assert.True(result.Applied);
        Assert.Equal(-0.05, result.BeatAdjustment, 6);
    }

    [Fact]
    public void Correct_LargeError_IsClampedToMaxStep()
    {
        // A near-half-beat error must not yank the grid; the step is clamped so a stray onset can't
        // jump the beat.
        var pll = new OnsetPhaseLock(gain: 1.0, maxBeatStep: 0.05);
        PhaseCorrection result = pll.Correct(beatPhaseAtOnset: 0.4);

        Assert.True(result.Applied);
        Assert.Equal(0.05, result.BeatAdjustment, 6);
    }

    [Fact]
    public void Correct_WithinDeadband_DoesNothing()
    {
        var pll = new OnsetPhaseLock(deadband: 0.02);
        Assert.False(pll.Correct(beatPhaseAtOnset: 0.01).Applied);
        Assert.False(pll.Correct(beatPhaseAtOnset: 0.99).Applied);
    }

    [Fact]
    public void Correct_RepeatedApplication_ConvergesPhaseToZero()
    {
        // The whole point: feeding a steady phase offset through the loop must shrink it to ~0, not
        // oscillate or diverge (a proper contraction).
        var pll = new OnsetPhaseLock(gain: 0.3, maxBeatStep: 0.5, deadband: 0.001);
        double phase = 0.35;
        for (int i = 0; i < 100; i++)
        {
            PhaseCorrection c = pll.Correct(phase);
            phase -= c.BeatAdjustment; // applying the correction removes that much phase error
            phase -= Math.Floor(phase);
        }

        Assert.True(Math.Min(phase, 1.0 - phase) < 0.01, $"phase did not converge, ended at {phase:F4}");
    }
}
