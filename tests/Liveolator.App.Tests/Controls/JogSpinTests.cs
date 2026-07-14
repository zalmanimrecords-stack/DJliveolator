using Liveolator.App.Controls;

namespace Liveolator.App.Tests.Controls;

/// <summary>
/// While its deck plays, the jog's centre medusa turns like a record. The spin angle is advanced purely from
/// elapsed time (<see cref="Jog.AdvanceSpin"/>) so it is deterministic and frame-rate independent, and it
/// wraps within one revolution so it never grows without bound.
/// </summary>
public sealed class JogSpinTests
{
    [Fact]
    public void Spin_advances_clockwise_over_time()
        => Assert.True(Jog.AdvanceSpin(0.0, 0.1) > 0.0);

    [Fact]
    public void Spin_wraps_within_one_revolution()
    {
        double afterManyTurns = Jog.AdvanceSpin(0.0, 100.0);
        Assert.InRange(afterManyTurns, 0.0, 2.0 * Math.PI);
    }

    [Fact]
    public void Zero_elapsed_time_holds_the_angle()
        => Assert.Equal(1.23, Jog.AdvanceSpin(1.23, 0.0), precision: 6);

    [Theory]
    [InlineData(double.NaN, 0.1)]
    [InlineData(0.5, double.NaN)]
    public void Invalid_input_is_treated_as_no_advance(double current, double deltaSeconds)
        => Assert.InRange(Jog.AdvanceSpin(current, deltaSeconds), 0.0, 2.0 * Math.PI);

    [Fact]
    public void One_second_turns_a_record_share_of_a_revolution()
    {
        // 33 1/3 RPM ~= 0.556 rev/s, so one second is a little over half a turn.
        double oneSecond = Jog.AdvanceSpin(0.0, 1.0);
        double revolutions = oneSecond / (2.0 * Math.PI);
        Assert.InRange(revolutions, 0.4, 0.7);
    }
}
