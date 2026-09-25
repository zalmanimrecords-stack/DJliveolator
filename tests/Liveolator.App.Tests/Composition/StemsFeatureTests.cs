using Liveolator.App.Composition;
using Liveolator.Core.Actions;
using Xunit;

namespace Liveolator.App.Tests.Composition;

public sealed class StemsFeatureTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("true", false)]
    public void IsOn_OnlyForTheExactValueOne(string? value, bool expected) =>
        Assert.Equal(expected, StemsFeature.IsOn(value));

    [Theory]
    [InlineData(PerformanceActionKind.DeckStemMute, true)]
    [InlineData(PerformanceActionKind.DeckStemGain, true)]
    [InlineData(PerformanceActionKind.DeckPlayPause, false)]
    public void IsStemAction_CoversBothStemKinds(PerformanceActionKind kind, bool expected) =>
        Assert.Equal(expected, StemsFeature.IsStemAction(kind));
}
