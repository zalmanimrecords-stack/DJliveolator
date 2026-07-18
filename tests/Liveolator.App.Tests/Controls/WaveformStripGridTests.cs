using Liveolator.App.Controls;
using Xunit;

namespace Liveolator.App.Tests.Controls;

/// <summary>
/// The waveform grid's bar / phrase placement (owner request: bar lines cyan, every 4th bar red). Pure
/// index math, so it verifies without a render; the colours themselves are checked in the render shot.
/// </summary>
public sealed class WaveformStripGridTests
{
    [Theory]
    [InlineData(0, false)]  // bar 1 → cyan anchor (first phrase's "1" stays cyan)
    [InlineData(4, false)]  // bar 2
    [InlineData(8, false)]  // bar 3
    [InlineData(12, false)] // bar 4
    [InlineData(16, true)]  // bar 5 → phrase start (red)
    [InlineData(20, false)] // bar 6
    [InlineData(28, false)] // bar 8
    [InlineData(32, true)]  // bar 9 → phrase start (red)
    [InlineData(1, false)]  // a beat, not a bar → never a phrase
    [InlineData(2, false)]
    public void IsPhraseDownbeat_MarksEveryPhraseStart(int index, bool expected)
        => Assert.Equal(expected, WaveformStrip.IsPhraseDownbeat(index, offset: 0, beatsPerBar: 4, barsPerPhrase: 4));

    [Fact]
    public void IsPhraseDownbeat_CountsBarsFromTheDownbeatOffset()
    {
        // Grid whose "one" sits on beat index 2: bar downbeats at 2, 6, 10, 14, 18 (bars 1–5). The phrase
        // start is bar 5 (index 18) → red; bar 1 (index 2) is the cyan anchor, and index 0 is not a bar.
        Assert.False(WaveformStrip.IsPhraseDownbeat(2, offset: 2, beatsPerBar: 4, barsPerPhrase: 4));
        Assert.True(WaveformStrip.IsPhraseDownbeat(18, offset: 2, beatsPerBar: 4, barsPerPhrase: 4));
        Assert.False(WaveformStrip.IsPhraseDownbeat(14, offset: 2, beatsPerBar: 4, barsPerPhrase: 4));
        Assert.False(WaveformStrip.IsPhraseDownbeat(0, offset: 2, beatsPerBar: 4, barsPerPhrase: 4));
    }

    [Fact]
    public void EveryBarDownbeat_IsEitherAPlainBarLineOrAPhrase_NeverBoth()
    {
        for (int i = 0; i < 64; i++)
        {
            bool isBar = WaveformStrip.IsBarDownbeat(i, offset: 0, beatsPerBar: 4);
            bool isPhrase = WaveformStrip.IsPhraseDownbeat(i, offset: 0, beatsPerBar: 4, barsPerPhrase: 4);
            if (isPhrase)
                Assert.True(isBar, $"index {i}: a phrase must also be a bar downbeat");
            if (!isBar)
                Assert.False(isPhrase, $"index {i}: a non-bar beat is never a phrase");
        }
    }
}
