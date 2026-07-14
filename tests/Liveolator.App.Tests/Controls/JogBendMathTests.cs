using Liveolator.App.Controls;
using Xunit;

namespace Liveolator.App.Tests.Controls;

/// <summary>
/// The pure drag→angular-velocity mapping behind the playing (pitch-bend) mode of the <see cref="Jog"/>
/// wheel: a rotation over a time interval becomes revolutions/second, clockwise positive, with the
/// interval clamped so a stalled or bursty pointer can't spike the bend. The velocity→bend policy itself
/// lives in Core's JogMath; this only locks down the kinematics.
/// </summary>
public class JogBendMathTests
{
    private const double Tau = 2.0 * System.Math.PI;

    [Fact]
    public void Full_turn_over_a_typical_frame_gives_the_expected_rev_per_second()
        => Assert.Equal(1.0 / 0.05, Jog.AngularVelocityRevPerSecond(Tau, 0.05), precision: 9); // 20 rev/s

    [Fact]
    public void Half_the_rotation_is_half_the_velocity()
        => Assert.Equal(0.5 / 0.05, Jog.AngularVelocityRevPerSecond(System.Math.PI, 0.05), precision: 9);

    [Fact]
    public void Counter_clockwise_rotation_is_negative()
        => Assert.True(Jog.AngularVelocityRevPerSecond(-System.Math.PI, 0.5) < 0.0);

    [Fact]
    public void Tiny_interval_is_clamped_so_velocity_cannot_spike()
    {
        // 1 rev in 1 ms would read as 1000 rev/s; the 4 ms floor caps it at 250 rev/s.
        Assert.Equal(1.0 / 0.004, Jog.AngularVelocityRevPerSecond(Tau, 0.001), precision: 6);
    }

    [Fact]
    public void Huge_interval_is_clamped_so_a_stalled_frame_reads_a_sane_velocity()
    {
        // 1 rev in 10 s is clamped to the 100 ms ceiling → 10 rev/s, not 0.1.
        Assert.Equal(1.0 / 0.100, Jog.AngularVelocityRevPerSecond(Tau, 10.0), precision: 6);
    }

    [Theory]
    [InlineData(double.NaN, 0.01)]
    [InlineData(0.5, double.NaN)]
    [InlineData(0.5, double.PositiveInfinity)]
    public void NonFiniteInput_IsZero(double deltaRadians, double dtSeconds)
        => Assert.Equal(0.0, Jog.AngularVelocityRevPerSecond(deltaRadians, dtSeconds));
}
