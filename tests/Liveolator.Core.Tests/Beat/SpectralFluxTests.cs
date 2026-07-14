using Liveolator.Core.Beat;
using Xunit;

namespace Liveolator.Core.Tests.Beat;

public class SpectralFluxTests
{
    [Fact]
    public void SumsOnlyPositiveChanges()
    {
        var prev = new[] { 1f, 1f, 1f, 1f };
        var cur = new[] { 2f, 1f, 4f, 0f }; // +1, 0, +3, -1 → 4

        Assert.Equal(4.0, SpectralFlux.Positive(prev, cur), precision: 6);
    }

    [Fact]
    public void RisingEnergyOnly_ReturnsZeroOnDecay()
    {
        var prev = new[] { 5f, 5f };
        var cur = new[] { 1f, 0f };

        Assert.Equal(0.0, SpectralFlux.Positive(prev, cur), precision: 6);
    }

    [Fact]
    public void MismatchedLengths_ReturnZero()
    {
        Assert.Equal(0.0, SpectralFlux.Positive(new float[3], new float[4]), precision: 9);
        Assert.Equal(0.0, SpectralFlux.Positive(new float[0], new float[0]), precision: 9);
    }
}
