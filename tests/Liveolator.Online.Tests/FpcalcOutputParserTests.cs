using Liveolator.Core.Enrichment;
using Liveolator.Online;
using Xunit;

namespace Liveolator.Online.Tests;

public class FpcalcOutputParserTests
{
    [Fact]
    public void Parse_ValidOutput_ReturnsFingerprintAndRoundedDuration()
    {
        const string json = """{ "duration": 570.49, "fingerprint": "AQADtEmkRYkkR..." }""";

        AudioFingerprint? result = FpcalcOutputParser.Parse(json);

        Assert.NotNull(result);
        Assert.Equal("AQADtEmkRYkkR...", result!.Fingerprint);
        Assert.Equal(570, result.DurationSeconds); // rounded from 570.49
    }

    [Fact]
    public void Parse_MissingFingerprint_ReturnsNull()
        => Assert.Null(FpcalcOutputParser.Parse("""{ "duration": 200 }"""));

    [Fact]
    public void Parse_MissingDuration_ReturnsNull()
        => Assert.Null(FpcalcOutputParser.Parse("""{ "fingerprint": "abc" }"""));

    [Fact]
    public void Parse_ZeroDuration_ReturnsNull()
        => Assert.Null(FpcalcOutputParser.Parse("""{ "duration": 0, "fingerprint": "abc" }"""));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    public void Parse_BlankOrInvalid_ReturnsNull(string json)
        => Assert.Null(FpcalcOutputParser.Parse(json));
}
