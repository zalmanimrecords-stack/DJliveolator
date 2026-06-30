using Liveolator.Media.Analysis;
using Xunit;

namespace Liveolator.Media.Tests;

/// <summary>
/// Parsing of the JSON emitted by <c>analyze_structure.py</c> — tested against captured sample stdout
/// strings, so no Python runtime is needed (mirrors FpcalcOutputParser's parse/IO split).
/// </summary>
public class StructureOutputParserTests
{
    private const string Sample = """
    {"sections":[
      {"startSeconds":0.0,"label":"intro"},
      {"startSeconds":16.0,"label":"buildup"},
      {"startSeconds":32.0,"label":"drop"},
      {"startSeconds":96.5,"label":"breakdown"},
      {"startSeconds":160.0,"label":"outro"}
    ],"analyzedWith":"librosa 0.10.2"}
    """;

    [Fact]
    public void Parse_ValidJson_ReturnsSectionsInOrder()
    {
        var result = StructureOutputParser.Parse(Sample);

        Assert.NotNull(result);
        Assert.Equal("librosa 0.10.2", result!.AnalyzedWith);
        Assert.Equal(5, result.Sections.Count);
        Assert.Equal(0.0, result.Sections[0].StartSeconds);
        Assert.Equal("intro", result.Sections[0].Label);
        Assert.Equal(96.5, result.Sections[3].StartSeconds);
        Assert.Equal("breakdown", result.Sections[3].Label);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{}")]                                   // no sections array
    [InlineData("""{"sections":[]}""")]                  // empty sections
    [InlineData("""{"sections":[{"label":"drop"}]}""")]  // section missing startSeconds
    [InlineData("[1,2,3]")]                               // wrong root kind
    public void Parse_InvalidOrIncomplete_ReturnsNull(string? json)
        => Assert.Null(StructureOutputParser.Parse(json));

    [Fact]
    public void Parse_MissingAnalyzedWith_DefaultsToLibrosa()
    {
        var result = StructureOutputParser.Parse("""{"sections":[{"startSeconds":1.0,"label":"drop"}]}""");
        Assert.NotNull(result);
        Assert.Equal("librosa", result!.AnalyzedWith);
    }

    [Fact]
    public void Parse_SkipsMalformedSectionsButKeepsValidOnes()
    {
        var result = StructureOutputParser.Parse("""
        {"sections":[{"startSeconds":1.0,"label":"drop"},{"label":"bad"},{"startSeconds":2.0,"label":"outro"}]}
        """);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Sections.Count);
    }
}
