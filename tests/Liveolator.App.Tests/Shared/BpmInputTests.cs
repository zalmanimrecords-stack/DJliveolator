using Liveolator.App.Features.Shared;
using Xunit;

namespace Liveolator.App.Tests.Shared;

public class BpmInputTests
{
    [Theory]
    [InlineData("120", 120.0)]
    [InlineData("128.5", 128.5)]
    [InlineData("40", 40.0)]    // lower bound inclusive
    [InlineData("300", 300.0)]  // upper bound inclusive
    public void TryParse_AcceptsMusicalTempos(string text, double expected)
    {
        Assert.True(BpmInput.TryParse(text, out double bpm));
        Assert.Equal(expected, bpm);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("39.9")]   // below the floor
    [InlineData("301")]    // above the ceiling
    [InlineData("9999")]   // the fat-finger case (doc 31 L4)
    [InlineData("")]
    [InlineData("abc")]
    public void TryParse_RejectsOutOfRangeOrJunk(string text)
        => Assert.False(BpmInput.TryParse(text, out _));
}
