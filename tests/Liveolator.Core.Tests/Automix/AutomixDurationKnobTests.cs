using Liveolator.Core.Automix;
using Xunit;

namespace Liveolator.Core.Tests.Automix;

public class AutomixDurationKnobTests
{
    [Theory]
    [InlineData(0.0, 2)]
    [InlineData(0.2, 4)]
    [InlineData(0.4, 8)]
    [InlineData(0.6, 16)]
    [InlineData(0.8, 32)]
    [InlineData(1.0, 64)]
    public void BarsFor_MapsKnobPositionToDetents(double knob, int expectedBars)
        => Assert.Equal(expectedBars, AutomixDurationKnob.BarsFor(knob));

    [Theory]
    [InlineData(-0.5, 2)]
    [InlineData(1.5, 64)]
    public void BarsFor_ClampsOutOfRangeKnob(double knob, int expectedBars)
        => Assert.Equal(expectedBars, AutomixDurationKnob.BarsFor(knob));

    [Fact]
    public void KnobFor_RoundTripsEveryDetent()
    {
        foreach (int bars in AutomixDurationKnob.DetentBars)
            Assert.Equal(bars, AutomixDurationKnob.BarsFor(AutomixDurationKnob.KnobFor(bars)));
    }

    [Fact]
    public void Detents_AreAllEvenBarCounts()
    {
        // The styles quantize their bass swap to the transition midpoint; an even bar count puts that
        // midpoint on a downbeat by construction — this invariant keeps the profiles stateless.
        foreach (int bars in AutomixDurationKnob.DetentBars)
            Assert.Equal(0, bars % 2);
    }

    [Fact]
    public void DefaultBars_IsADetent()
        => Assert.Contains(AutomixDurationKnob.DefaultBars, AutomixDurationKnob.DetentBars);
}
