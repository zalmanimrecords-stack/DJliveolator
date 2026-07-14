using Liveolator.Core.Audio;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

/// <summary>
/// Verifies the pure channel-pair mapping shared by the Settings picker and the BASS speaker-flag
/// resolver: how many stereo pairs a card exposes, each pair's first channel + label, and clamping a
/// stale pair choice back into a device's real range.
/// </summary>
public sealed class OutputChannelPairTests
{
    [Theory]
    [InlineData(1, 1)]   // mono / unknown -> still one pair so the picker is never empty
    [InlineData(2, 1)]   // stereo -> 1/2 only
    [InlineData(4, 2)]   // CMD STUDIO 2A -> 1/2 + 3/4
    [InlineData(8, 4)]   // full eight channels -> four pairs
    [InlineData(16, 4)]  // more than BASS addresses -> capped at four pairs
    public void PairCount_IsHalfTheChannels_FlooredAtOne_CappedAtMax(int channels, int expected)
        => Assert.Equal(expected, OutputChannelPair.PairCount(channels));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 3)]
    [InlineData(2, 5)]
    [InlineData(3, 7)]
    public void FirstChannel_IsOneBasedStartOfThePair(int pairIndex, int expectedFirstChannel)
        => Assert.Equal(expectedFirstChannel, OutputChannelPair.FirstChannel(pairIndex));

    [Theory]
    [InlineData(0, "Outputs 1/2")]
    [InlineData(1, "Outputs 3/4")]
    [InlineData(3, "Outputs 7/8")]
    public void Label_ReadsAsAChannelPair(int pairIndex, string expected)
        => Assert.Equal(expected, OutputChannelPair.Label(pairIndex));

    [Theory]
    [InlineData(1, 4, 1)]    // 3/4 valid on a 4-channel card -> kept
    [InlineData(1, 2, 0)]    // 3/4 invalid on a stereo card -> falls back to 1/2
    [InlineData(9, 8, 3)]    // beyond the four pairs an 8-channel card has -> clamped to the last (7/8)
    [InlineData(-2, 4, 0)]   // negative -> first pair
    public void Clamp_KeepsValidPairs_AndFallsBackWhenTheCardLacksThem(
        int pairIndex, int channels, int expected)
        => Assert.Equal(expected, OutputChannelPair.Clamp(pairIndex, channels));
}
