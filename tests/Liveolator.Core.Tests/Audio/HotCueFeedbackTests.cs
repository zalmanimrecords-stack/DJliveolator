using Liveolator.Core.Audio;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

/// <summary>Round-trip + tolerance of the DeckHotCue feedback Argument codec (index + label/color/auto).</summary>
public class HotCueFeedbackTests
{
    [Fact]
    public void Encode_Decode_RoundTripsFullMetadata()
    {
        var info = new HotCueInfo(IsSet: true, Label: "Drop", Color: 0xFF3B30, IsAuto: true);

        string arg = HotCueFeedback.Encode(3, info);
        Assert.True(HotCueFeedback.TryDecode(arg, out int index, out HotCueInfo back));

        Assert.Equal(3, index);
        Assert.Equal("Drop", back.Label);
        Assert.Equal(0xFF3B30, back.Color);
        Assert.True(back.IsAuto);
    }

    [Fact]
    public void Encode_Decode_RoundTripsAnUnlabeledManualCue()
    {
        string arg = HotCueFeedback.Encode(5, new HotCueInfo(IsSet: true));
        Assert.True(HotCueFeedback.TryDecode(arg, out int index, out HotCueInfo back));

        Assert.Equal(5, index);
        Assert.Null(back.Label);
        Assert.Null(back.Color);
        Assert.False(back.IsAuto);
    }

    [Fact]
    public void TryDecode_BareIndex_IsTolerated_AsASetCueWithNoMetadata()
    {
        // Historical / index-only feedback must still light the right pad.
        Assert.True(HotCueFeedback.TryDecode("2", out int index, out HotCueInfo back));
        Assert.Equal(2, index);
        Assert.Null(back.Label);
        Assert.False(back.IsAuto);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-index")]
    public void TryDecode_Malformed_ReturnsFalse(string? argument)
        => Assert.False(HotCueFeedback.TryDecode(argument, out _, out _));

    [Fact]
    public void Decode_PreservesLabelsContainingTheSeparator()
    {
        // Label is the final field, so a stray separator inside it survives the split.
        string arg = HotCueFeedback.Encode(1, new HotCueInfo(IsSet: true, Label: "Drop|Verse"));
        Assert.True(HotCueFeedback.TryDecode(arg, out _, out HotCueInfo back));
        Assert.Equal("Drop|Verse", back.Label);
    }
}
