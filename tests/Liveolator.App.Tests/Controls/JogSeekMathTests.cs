using Liveolator.App.Controls;
using Xunit;

namespace Liveolator.App.Tests.Controls;

/// <summary>
/// The pure drag→seek mapping behind the <see cref="Jog"/> wheel: a clockwise rotation advances the
/// playhead, a counter-clockwise rotation rewinds it, and the result is always a valid 0..1 track
/// fraction. Tested in isolation (no pointer simulation) so the seek arithmetic is locked down; the
/// rendered wheel is covered by the UiShots harness.
/// </summary>
public class JogSeekMathTests
{
    private const double Tau = 2.0 * System.Math.PI;

    [Fact]
    public void No_rotation_keeps_the_playhead_where_it_was()
    {
        Assert.Equal(0.5, Jog.ScrubFraction(0.5, accumulatedRadians: 0.0), precision: 9);
    }

    [Fact]
    public void Clockwise_full_turn_advances_by_one_turns_worth()
    {
        double expected = 0.5 + Jog.SeekTrackFractionPerTurn;
        Assert.Equal(expected, Jog.ScrubFraction(0.5, accumulatedRadians: Tau), precision: 9);
    }

    [Fact]
    public void Counter_clockwise_full_turn_rewinds_by_one_turns_worth()
    {
        double expected = 0.5 - Jog.SeekTrackFractionPerTurn;
        Assert.Equal(expected, Jog.ScrubFraction(0.5, accumulatedRadians: -Tau), precision: 9);
    }

    [Fact]
    public void Half_turn_moves_half_a_turns_worth()
    {
        double expected = 0.5 + (Jog.SeekTrackFractionPerTurn / 2.0);
        Assert.Equal(expected, Jog.ScrubFraction(0.5, accumulatedRadians: System.Math.PI), precision: 9);
    }

    [Fact]
    public void Result_never_exceeds_the_end_of_the_track()
    {
        Assert.Equal(1.0, Jog.ScrubFraction(0.95, accumulatedRadians: Tau * 4), precision: 9);
    }

    [Fact]
    public void Result_never_goes_before_the_start_of_the_track()
    {
        Assert.Equal(0.0, Jog.ScrubFraction(0.05, accumulatedRadians: -Tau * 4), precision: 9);
    }
}
