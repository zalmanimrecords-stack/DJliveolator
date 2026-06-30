using Liveolator.Core.Analysis.Stems;
using Liveolator.Media.Analysis;
using Xunit;

namespace Liveolator.Media.Tests;

/// <summary>
/// Parsing of the JSON manifest emitted by <c>separate_stems.py</c> — tested against captured sample
/// stdout strings, so no Python runtime is needed (mirrors <see cref="StructureOutputParserTests"/>).
/// </summary>
public class StemManifestParserTests
{
    private const string Source = "/music/song.mp3";

    private const string Sample = """
    {"model":"umxhq","stems":{
      "drums":"/c/drums.flac",
      "bass":"/c/bass.flac",
      "vocals":"/c/vocals.flac",
      "other":"/c/other.flac"}}
    """;

    [Fact]
    public void Parse_ValidJson_ReturnsCompleteSet()
    {
        var result = StemManifestParser.Parse(Sample, Source);

        Assert.NotNull(result);
        Assert.Equal("umxhq", result!.ModelId);
        Assert.Equal(Source, result.SourcePath);
        Assert.True(result.IsComplete);
        Assert.Equal("/c/drums.flac", result.StemPaths[StemKind.Drums]);
        Assert.Equal("/c/other.flac", result.StemPaths[StemKind.Other]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{}")]                                          // no stems object
    [InlineData("""{"stems":{}}""")]                            // empty stems
    [InlineData("""{"stems":{"drums":"/c/d.flac"}}""")]         // incomplete (missing 3)
    [InlineData("[1,2,3]")]                                     // wrong root kind
    public void Parse_InvalidOrIncomplete_ReturnsNull(string? json)
        => Assert.Null(StemManifestParser.Parse(json, Source));

    [Fact]
    public void Parse_BlankSourcePath_ReturnsNull()
        => Assert.Null(StemManifestParser.Parse(Sample, "  "));

    [Fact]
    public void Parse_MissingModel_DefaultsToUmxhq()
    {
        string json = """
        {"stems":{"drums":"/c/d.flac","bass":"/c/b.flac","vocals":"/c/v.flac","other":"/c/o.flac"}}
        """;
        var result = StemManifestParser.Parse(json, Source);
        Assert.NotNull(result);
        Assert.Equal("umxhq", result!.ModelId);
    }

    [Fact]
    public void SerializeThenParse_RoundTrips()
    {
        var original = StemManifestParser.Parse(Sample, Source)!;
        var round = StemManifestParser.Parse(StemManifestParser.Serialize(original), Source);

        Assert.NotNull(round);
        Assert.Equal(original.ModelId, round!.ModelId);
        Assert.Equal(original.StemPaths[StemKind.Vocals], round.StemPaths[StemKind.Vocals]);
    }
}
