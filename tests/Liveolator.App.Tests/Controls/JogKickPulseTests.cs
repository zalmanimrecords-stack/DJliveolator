using Liveolator.App.Controls;

namespace Liveolator.App.Tests.Controls;

/// <summary>
/// The jog rim-glow pulse (doc 19): it peaks (1) exactly on a beat line — the kick, for 4-on-the-floor —
/// and decays toward 0 before the next beat, so the frame flashes green on every kick.
/// </summary>
public sealed class JogKickPulseTests
{
    private static readonly double[] Grid = { 0.0, 0.25, 0.5, 0.75, 1.0 };

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    public void Pulse_is_full_on_a_beat_line(double progress)
        => Assert.Equal(1.0, Jog.KickPulse(progress, Grid), precision: 6);

    [Fact]
    public void Pulse_decays_after_the_beat()
    {
        double justAfter = Jog.KickPulse(0.27, Grid);   // just past a beat line
        double midway = Jog.KickPulse(0.375, Grid);     // halfway to the next
        double justBefore = Jog.KickPulse(0.49, Grid);  // about to hit the next

        Assert.InRange(justAfter, 0.5, 1.0);
        Assert.InRange(midway, 0.1, 0.5);
        Assert.InRange(justBefore, 0.0, 0.05);
        Assert.True(justAfter > midway && midway > justBefore);
    }

    [Theory]
    [InlineData(null)]
    public void No_grid_means_no_glow(double[]? grid)
        => Assert.Equal(0.0, Jog.KickPulse(0.3, grid));

    [Fact]
    public void Single_beat_line_means_no_glow()
        => Assert.Equal(0.0, Jog.KickPulse(0.3, new[] { 0.5 }));

    [Fact]
    public void Past_the_last_beat_line_no_glow()
        => Assert.Equal(0.0, Jog.KickPulse(1.0, Grid));
}
