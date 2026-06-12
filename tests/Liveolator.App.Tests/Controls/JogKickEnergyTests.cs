using Liveolator.App.Controls;

namespace Liveolator.App.Tests.Controls;

/// <summary>
/// The jog rim-glow intensity is sampled from the track's low-frequency (kick) band at the playhead — so
/// the glow comes from the actual sound. It peaks where the kick hits and stays dim on the quiet low-end.
/// </summary>
public sealed class JogKickEnergyTests
{
    // index = round(progress * (count-1)); a spike at index 2 of 4 sits at progress 2/3.
    private static readonly float[] Kicks = { 0.05f, 0.05f, 1.0f, 0.05f };

    [Fact]
    public void Full_on_a_kick_transient()
        => Assert.Equal(1.0, Jog.KickEnergyAt(2.0 / 3.0, Kicks), precision: 6);

    [Fact]
    public void Dim_between_kicks()
    {
        double quiet = Jog.KickEnergyAt(0.0, Kicks); // low-end floor, gamma'd down
        Assert.InRange(quiet, 0.0, 0.05);
        Assert.True(Jog.KickEnergyAt(2.0 / 3.0, Kicks) > quiet);
    }

    [Fact]
    public void Gamma_emphasises_strong_over_weak()
    {
        // 0.5^2 = 0.25 — a mid-strength low-end reads clearly dimmer than a full kick.
        Assert.Equal(0.25, Jog.KickEnergyAt(0.0, new[] { 0.5f }), precision: 6);
    }

    [Fact]
    public void Out_of_range_energy_is_clamped()
        => Assert.Equal(1.0, Jog.KickEnergyAt(0.0, new[] { 2.0f }), precision: 6);

    [Theory]
    [InlineData(null)]
    public void No_kick_data_means_no_glow(float[]? kicks)
        => Assert.Equal(0.0, Jog.KickEnergyAt(0.4, kicks));

    [Fact]
    public void Empty_kick_data_means_no_glow()
        => Assert.Equal(0.0, Jog.KickEnergyAt(0.4, System.Array.Empty<float>()));
}
