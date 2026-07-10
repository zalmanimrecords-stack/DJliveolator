using Liveolator.Core.Audio;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

public class JogMathTests
{
    // Standard DJ jog feel: a near-still wheel must never detune the deck, a firm turn bends
    // proportionally, and a hard spin saturates at the pitch rail — in both directions.
    private const double Gain = 0.04;       // bend fraction per rev/s
    private const double Deadzone = 0.05;   // rev/s
    private const double Max = 0.08;        // ±8 % pitch rail

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.04)]
    [InlineData(-0.049)]
    public void BelowDeadzone_ReturnsZero(double revPerSecond)
        => Assert.Equal(0.0, JogMath.BendFraction(revPerSecond, Gain, Deadzone, Max));

    [Fact]
    public void ModestTurn_ScalesLinearlyByGain()
        => Assert.Equal(0.02, JogMath.BendFraction(0.5, Gain, Deadzone, Max), precision: 6);

    [Fact]
    public void FastSpin_SaturatesAtTheMaxFraction()
        => Assert.Equal(Max, JogMath.BendFraction(5.0, Gain, Deadzone, Max), precision: 6);

    [Fact]
    public void CounterClockwiseTurn_BendsNegative()
        => Assert.Equal(-0.04, JogMath.BendFraction(-1.0, Gain, Deadzone, Max), precision: 6);

    [Fact]
    public void FastCounterClockwiseSpin_SaturatesAtNegativeMax()
        => Assert.Equal(-Max, JogMath.BendFraction(-5.0, Gain, Deadzone, Max), precision: 6);

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteVelocity_ReturnsZero(double revPerSecond)
        => Assert.Equal(0.0, JogMath.BendFraction(revPerSecond, Gain, Deadzone, Max));
}
