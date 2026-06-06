using System.Collections.Generic;
using Liveolator.App.Features.Live.Modules;
using Xunit;

namespace Liveolator.App.Tests.Live.Modules;

public sealed class BeatGridCalculatorTests
{
    [Fact]
    public void BeatFractions_SpacesLinesAtTheBeatInterval_FromTheStart()
    {
        // 120 BPM = 0.5 s/beat; an 8 s track has beats at 0,0.5,1,…,8 s → 17 lines (0..16), each 1/16 apart.
        IReadOnlyList<double> grid = BeatGridCalculator.BeatFractions(bpm: 120, durationSeconds: 8);

        Assert.Equal(17, grid.Count);
        Assert.Equal(0.0, grid[0], 6);
        Assert.Equal(1.0 / 16, grid[1], 6);
        Assert.Equal(0.5, grid[8], 6);
        Assert.Equal(1.0, grid[16], 6);
    }

    [Fact]
    public void BeatFractions_DoesNotEmitLinesPastTheTrackEnd()
    {
        // 120 BPM, 7.25 s → last whole beat at 7.0 s (beat 14); 7.5 s would overshoot, so it is excluded.
        IReadOnlyList<double> grid = BeatGridCalculator.BeatFractions(bpm: 120, durationSeconds: 7.25);

        Assert.Equal(15, grid.Count); // beats 0..14
        Assert.All(grid, f => Assert.InRange(f, 0.0, 1.0));
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(-130, 8)]
    [InlineData(120, 0)]
    [InlineData(120, -4)]
    [InlineData(double.NaN, 8)]
    [InlineData(120, double.NaN)]
    [InlineData(double.PositiveInfinity, 8)]
    public void BeatFractions_ReturnsEmpty_OnUnusableInputs(double bpm, double durationSeconds)
    {
        Assert.Empty(BeatGridCalculator.BeatFractions(bpm, durationSeconds));
    }

    [Fact]
    public void BeatFractions_CapsLineCount_OnAPathologicalTempo()
    {
        // A 600 s track at 9000 BPM would be 90k beats; the calculator caps the line count.
        IReadOnlyList<double> grid = BeatGridCalculator.BeatFractions(bpm: 9000, durationSeconds: 600);

        Assert.True(grid.Count <= 4_097, $"expected the grid to be capped, was {grid.Count}");
    }
}
