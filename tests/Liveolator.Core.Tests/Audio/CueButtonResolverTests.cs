using Liveolator.Core.Audio;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

/// <summary>CDJ back-to-cue decision logic for the deck Cue button (A5).</summary>
public class CueButtonResolverTests
{
    [Fact]
    public void Playing_AlwaysReturnsToCue()
    {
        Assert.Equal(
            CueButtonAction.ReturnToCue,
            CueButtonResolver.Resolve(isPlaying: true, currentPositionFraction: 0.6, cuePositionFraction: 0.2));
    }

    [Fact]
    public void Playing_WithNoCueSet_ReturnsToCue_FallsBackToStart()
    {
        Assert.Equal(
            CueButtonAction.ReturnToCue,
            CueButtonResolver.Resolve(isPlaying: true, currentPositionFraction: 0.6, cuePositionFraction: null));
    }

    [Fact]
    public void PausedWithNoCueSet_DropsCueHere()
    {
        Assert.Equal(
            CueButtonAction.SetCueHere,
            CueButtonResolver.Resolve(isPlaying: false, currentPositionFraction: 0.35, cuePositionFraction: null));
    }

    [Fact]
    public void PausedAwayFromCue_DropsANewCueHere()
    {
        Assert.Equal(
            CueButtonAction.SetCueHere,
            CueButtonResolver.Resolve(isPlaying: false, currentPositionFraction: 0.5, cuePositionFraction: 0.2));
    }

    [Fact]
    public void PausedAtCue_ReturnsToCue_Idempotent()
    {
        Assert.Equal(
            CueButtonAction.ReturnToCue,
            CueButtonResolver.Resolve(isPlaying: false, currentPositionFraction: 0.2, cuePositionFraction: 0.2));
    }

    [Fact]
    public void PausedNearlyAtCue_WithinTolerance_ReturnsToCue()
    {
        Assert.Equal(
            CueButtonAction.ReturnToCue,
            CueButtonResolver.Resolve(
                isPlaying: false, currentPositionFraction: 0.2 + 5e-5, cuePositionFraction: 0.2));
    }

    [Fact]
    public void PausedJustOutsideTolerance_SetsCueHere()
    {
        Assert.Equal(
            CueButtonAction.SetCueHere,
            CueButtonResolver.Resolve(
                isPlaying: false, currentPositionFraction: 0.2 + 1e-3, cuePositionFraction: 0.2));
    }
}
