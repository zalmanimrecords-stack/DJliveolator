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
    public void BeatFractions_AnchorsTheGridOnTheFirstBeat()
    {
        // 120 BPM (0.5 s/beat), 8 s track, first beat at 1.0 s → lines at 1.0,1.5,…,8.0 s. Index 0 is the
        // first beat (a bar downbeat), so the grid sits on the kicks rather than on the raw track start.
        IReadOnlyList<double> grid =
            BeatGridCalculator.BeatFractions(bpm: 120, durationSeconds: 8, firstBeatSeconds: 1.0);

        Assert.Equal(1.0 / 8, grid[0], 6);
        Assert.Equal(1.5 / 8, grid[1], 6);
        Assert.Equal(15, grid.Count); // 1.0..8.0 s, step 0.5 s
    }

    [Fact]
    public void BeatFractions_FallsBackToTheStart_OnAnOutOfRangeAnchor()
    {
        // An anchor at/after the track end is nonsense → anchor at the start (the pre-anchor behaviour).
        IReadOnlyList<double> grid =
            BeatGridCalculator.BeatFractions(bpm: 120, durationSeconds: 8, firstBeatSeconds: 99);

        Assert.Equal(0.0, grid[0], 6);
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

    // --- DownbeatBarOffset: which grid line carries the red bar marker (the "one") ---

    [Fact]
    public void DownbeatBarOffset_IsZero_WhenTheDownbeatEqualsTheFirstBeat()
    {
        // Downbeat == first beat → index 0 is the bar start (the prior behaviour).
        Assert.Equal(0, BeatGridCalculator.DownbeatBarOffset(bpm: 120, firstBeatSeconds: 1.0, downbeatSeconds: 1.0));
    }

    [Theory]
    // 120 BPM = 0.5 s/beat, first beat at 0. Downbeat N beats later → offset N (mod 4).
    [InlineData(0.5, 1)]   // one beat after the anchor → beat 1 of the bar is the one
    [InlineData(1.0, 2)]   // two beats after
    [InlineData(1.5, 3)]   // three beats after
    [InlineData(2.0, 0)]   // a full bar later → folds back to 0
    [InlineData(2.5, 1)]   // wraps past one bar
    public void DownbeatBarOffset_FoldsTheDownbeatIntoABeatOfTheBar(double downbeatSeconds, int expected)
    {
        Assert.Equal(expected,
            BeatGridCalculator.DownbeatBarOffset(bpm: 120, firstBeatSeconds: 0.0, downbeatSeconds));
    }

    [Fact]
    public void DownbeatBarOffset_NormalizesWhenTheDownbeatPrecedesTheFirstBeat()
    {
        // first beat 1.5 s, downbeat 0.5 s (the bar started before the beat anchor) at 0.5 s/beat:
        // (0.5 - 1.5)/0.5 = -2 → folded into [0,4) = 2.
        Assert.Equal(2,
            BeatGridCalculator.DownbeatBarOffset(bpm: 120, firstBeatSeconds: 1.5, downbeatSeconds: 0.5));
    }

    [Theory]
    [InlineData(0, 0.0, 1.0)]                 // no tempo
    [InlineData(120, 0.0, 0.0)]               // no downbeat known → index 0 is the bar start
    [InlineData(120, 0.0, -1.0)]              // negative downbeat → treated as unknown
    [InlineData(120, double.NaN, 1.0)]        // bad anchor
    [InlineData(120, 0.0, double.NaN)]        // bad downbeat
    public void DownbeatBarOffset_FallsBackToZero_OnUnusableInputs(
        double bpm, double firstBeatSeconds, double downbeatSeconds)
    {
        Assert.Equal(0, BeatGridCalculator.DownbeatBarOffset(bpm, firstBeatSeconds, downbeatSeconds));
    }
}
