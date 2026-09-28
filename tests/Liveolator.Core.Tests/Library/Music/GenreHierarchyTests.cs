using Liveolator.Core.Library.Music;
using Xunit;

namespace Liveolator.Core.Tests.Library.Music;

public class GenreHierarchyTests
{
    [Fact]
    public void UnmappedGenre_StaysTopLevel()
    {
        var hierarchy = new GenreHierarchy();

        Assert.Equal(
            new[] { "House", "Techno" },
            hierarchy.TopLevelGenres(new[] { "House", "Techno" }));
        Assert.Null(hierarchy.ParentOf("House"));
    }

    [Fact]
    public void MappedGenre_DisappearsFromTopLevel_AppearsUnderParentsChildren()
    {
        var hierarchy = new GenreHierarchy().SetParent("Deep House", "House");

        Assert.Equal(
            new[] { "House" },
            hierarchy.TopLevelGenres(new[] { "House", "Deep House" }));
        Assert.Equal(new[] { "Deep House" }, hierarchy.ChildrenOf("House"));
        Assert.Equal("House", hierarchy.ParentOf("Deep House"));
    }

    [Fact]
    public void SetParent_Null_ReturnsGenreToTopLevel()
    {
        var withParent = new GenreHierarchy().SetParent("Deep House", "House");

        var cleared = withParent.SetParent("Deep House", null);

        Assert.Null(cleared.ParentOf("Deep House"));
        Assert.Empty(cleared.ChildrenOf("House"));
        // The instance passed to SetParent(null) is itself untouched (immutable).
        Assert.Equal("House", withParent.ParentOf("Deep House"));
    }

    [Fact]
    public void SetParent_DirectCycle_IsRejected_StateUnchanged()
    {
        var hierarchy = new GenreHierarchy().SetParent("Deep House", "House");
        var mappingsBefore = new Dictionary<string, string>(hierarchy.Mappings);

        Assert.Throws<ArgumentException>(() => hierarchy.SetParent("House", "House"));

        Assert.Equal(mappingsBefore, hierarchy.Mappings);
    }

    [Fact]
    public void SetParent_TransitiveCycle_IsRejected_StateUnchanged()
    {
        // House -> Electronic, Deep House -> House. Now try Electronic -> Deep House, which would
        // close the loop Electronic -> Deep House -> House -> Electronic.
        var hierarchy = new GenreHierarchy()
            .SetParent("House", "Electronic")
            .SetParent("Deep House", "House");
        var mappingsBefore = new Dictionary<string, string>(hierarchy.Mappings);

        Assert.Throws<ArgumentException>(() => hierarchy.SetParent("Electronic", "Deep House"));

        Assert.Equal(mappingsBefore, hierarchy.Mappings);
    }

    [Fact]
    public void SetParent_ToItself_IsRejected()
    {
        var hierarchy = new GenreHierarchy();

        Assert.Throws<ArgumentException>(() => hierarchy.SetParent("House", "House"));
        Assert.Null(hierarchy.ParentOf("House"));
    }
}
