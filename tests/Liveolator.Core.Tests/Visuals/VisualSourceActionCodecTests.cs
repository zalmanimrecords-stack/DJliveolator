using Liveolator.Core.Visuals;
using Xunit;

namespace Liveolator.Core.Tests.Visuals;

public sealed class VisualSourceActionCodecTests
{
    [Fact]
    public void Encode_then_TryDecode_round_trips_a_generator_source()
    {
        var source = new VisualSourceRef(VisualSourceKind.Generator, "core/vu-meter");

        Assert.True(VisualSourceActionCodec.TryDecode(VisualSourceActionCodec.Encode(source), out VisualSourceRef? decoded));
        Assert.Equal(source, decoded);
    }

    [Fact]
    public void TryDecode_accepts_a_None_source_despite_its_empty_reference()
    {
        // None clears a layer and carries no reference, so it must survive the round trip even though
        // every other kind requires a non-empty reference.
        Assert.True(VisualSourceActionCodec.TryDecode(VisualSourceActionCodec.Encode(VisualSourceRef.None), out VisualSourceRef? decoded));
        Assert.Equal(VisualSourceKind.None, decoded!.Kind);
    }

    [Fact]
    public void TryDecode_rejects_a_non_None_source_with_an_empty_reference()
    {
        string payload = VisualSourceActionCodec.Encode(new VisualSourceRef(VisualSourceKind.Image, string.Empty));

        Assert.False(VisualSourceActionCodec.TryDecode(payload, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-json")]
    public void TryDecode_rejects_missing_or_malformed_payloads(string? payload)
    {
        Assert.False(VisualSourceActionCodec.TryDecode(payload, out _));
    }
}
