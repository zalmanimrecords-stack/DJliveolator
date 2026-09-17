using Liveolator.Core.Library.Music;
using System.Linq;
using Xunit;

namespace Liveolator.Core.Tests.Library.Music;

/// <summary>
/// The cases below are the REAL values measured from the owner's catalog (179 tracks, 2026-09-13),
/// not invented ones. Four separators occur in practice — <c>|</c>, <c>,</c>, <c>/</c> and <c>;</c> —
/// and exact-string equality shatters them, which is how a "psytrance" pool once admitted techno.
/// </summary>
public class GenreTagTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("  ,  / ; | ")]
    public void Normalize_NoUsableText_IsEmpty(string? genre)
        => Assert.Empty(GenreTag.Normalize(genre));

    [Theory]
    [InlineData("Psytrance")]
    [InlineData("Psy-Trance")]
    [InlineData("Psy Trance")]
    [InlineData("  PSYTRANCE  ")]
    public void Normalize_SpellingVariantsCollapseToOneToken(string genre)
        => Assert.Equal(new[] { "psytrance" }, GenreTag.Normalize(genre));

    [Fact]
    public void Normalize_SplitsOnEverySeparatorSeenInRealTags()
    {
        Assert.Equal(
            new[] { "goatrance", "psytrance" },
            GenreTag.Normalize("Goa Trance/Psytrance").OrderBy(t => t, StringComparer.Ordinal));

        Assert.Equal(
            new[] { "electronic", "goa", "psychedelictrance" },
            GenreTag.Normalize("Goa, Psychedelic Trance, Electronic").OrderBy(t => t, StringComparer.Ordinal));

        Assert.Equal(
            new[] { "deep", "dub", "hypnotic", "raw", "techno" },
            GenreTag.Normalize("Techno ;Raw / Deep / Hypnotic; | Dub").OrderBy(t => t, StringComparer.Ordinal));
    }

    /// <summary>
    /// The owner's actual pain: these two were different genres under exact equality, so a set built
    /// from one silently refused the other. Multi-value tags must OVERLAP, not match whole-string.
    /// </summary>
    [Fact]
    public void Normalize_MultiValueTagOverlapsItsOwnSingleValueForm()
    {
        var single = GenreTag.Normalize("Melodic House & Techno");
        var multi = GenreTag.Normalize("Melodic House & Techno | Melodic Techno");

        Assert.Contains("melodichousetechno", single);
        Assert.Contains("melodichousetechno", multi);
        Assert.True(GenreTag.Intersects(single, multi));
    }

    [Fact]
    public void Intersects_IsFalseWhenEitherSideIsEmpty()
    {
        var psy = GenreTag.Normalize("Psytrance");

        Assert.False(GenreTag.Intersects(psy, GenreTag.Normalize(null)));
        Assert.False(GenreTag.Intersects(GenreTag.Normalize(null), psy));
        Assert.False(GenreTag.Intersects(GenreTag.Normalize(null), GenreTag.Normalize(null)));
    }

    [Fact]
    public void Intersects_IsFalseForGenuinelyDifferentGenres()
        => Assert.False(GenreTag.Intersects(
            GenreTag.Normalize("Psytrance"),
            GenreTag.Normalize("Melodic House & Techno")));
}
