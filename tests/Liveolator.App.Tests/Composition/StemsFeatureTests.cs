using Liveolator.App.Composition;
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
}
