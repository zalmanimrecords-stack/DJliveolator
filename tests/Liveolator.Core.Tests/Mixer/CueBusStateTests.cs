using Liveolator.Core.Mixer;
using Xunit;

namespace Liveolator.Core.Tests.Mixer;

public class CueBusStateTests
{
    private const double Tol = 1e-9;

    [Fact]
    public void Default_IsFullLevel_FullCueBlend()
    {
        Assert.Equal(1.0, CueBusState.Default.Level, Tol);
        Assert.Equal(CueBusState.FullCue, CueBusState.Default.Mix, Tol);
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.3, 0.3)]
    [InlineData(2.0, 1.0)]
    public void WithLevel_Clamps(double input, double expected)
        => Assert.Equal(expected, CueBusState.Default.WithLevel(input).Level, Tol);

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.7, 0.7)]
    [InlineData(2.0, 1.0)]
    public void WithMix_Clamps(double input, double expected)
        => Assert.Equal(expected, CueBusState.Default.WithMix(input).Mix, Tol);

    [Fact]
    public void MixerState_Default_CarriesDefaultCueBus()
    {
        Assert.Equal(CueBusState.Default, MixerState.Default.CueBus);
    }

    [Fact]
    public void WithCueBus_ReplacesBusOnly()
    {
        MixerState next = MixerState.Default.WithCueBus(new CueBusState(Level: 0.4, Mix: 0.6));

        Assert.Equal(0.4, next.CueBus.Level, Tol);
        Assert.Equal(0.6, next.CueBus.Mix, Tol);
        Assert.Equal(MixerState.Default.Crossfader, next.Crossfader, Tol);
    }
}
