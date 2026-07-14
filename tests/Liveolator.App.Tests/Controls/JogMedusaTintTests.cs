using Liveolator.App.Controls;

namespace Liveolator.App.Tests.Controls;

/// <summary>
/// The jog's centre medusa takes a subtle red wash driven by the kick/bass pulse at the playhead (the same
/// <see cref="Jog.KickEnergyAt"/> signal that lights the rim glow). The wash is 0 with no bass, scales with
/// the pulse, and is capped so it stays a tint — never a full repaint — of the image underneath.
/// </summary>
public sealed class JogMedusaTintTests
{
    [Fact]
    public void No_bass_means_no_tint()
        => Assert.Equal(0.0, Jog.BassTintStrength(0.0), precision: 6);

    [Fact]
    public void Full_kick_reaches_the_cap_but_stays_subtle()
    {
        double full = Jog.BassTintStrength(1.0);
        Assert.Equal(Jog.MaxBassTint, full, precision: 6);
        Assert.True(full <= 0.6, "the bass wash must stay a tint, not a full red repaint");
    }

    [Fact]
    public void Stronger_bass_gives_a_stronger_tint()
        => Assert.True(Jog.BassTintStrength(0.8) > Jog.BassTintStrength(0.2));

    [Theory]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Over_range_pulse_is_clamped_to_the_cap(double pulse)
        => Assert.Equal(Jog.MaxBassTint, Jog.BassTintStrength(pulse), precision: 6);

    [Theory]
    [InlineData(-0.5)]
    [InlineData(double.NaN)]
    public void Invalid_pulse_means_no_tint(double pulse)
        => Assert.Equal(0.0, Jog.BassTintStrength(pulse), precision: 6);
}
