using Liveolator.Core.Audio;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

public sealed class DeckKickOnsetCodecTests
{
    [Fact]
    public void EveryKickOfALongTrack_ReachesTheEngine()
    {
        // 30 minutes at 145 BPM: the engine must phase-lock on the same kicks the comb draws, to the last one.
        double[] kicks = Enumerable.Range(0, 4350).Select(k => 0.2 + (k * 60.0 / 145.0)).ToArray();

        IReadOnlyList<double> decoded = DeckKickOnsetCodec.Decode(DeckKickOnsetCodec.Encode(kicks));

        Assert.Equal(kicks.Length, decoded.Count);
        Assert.Equal(kicks[^1], decoded[^1], precision: 6);
    }
}
