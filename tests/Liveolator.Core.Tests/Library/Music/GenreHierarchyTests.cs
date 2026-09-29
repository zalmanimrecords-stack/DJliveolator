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
        // House -> Electronic. The old (unbounded-depth) model would have let "Deep House -> House" go
        // through and only caught the eventual transitive cycle (Electronic -> House -> Deep House ->
        // ... -> Electronic) once the closing edge was added. The Phase 3 depth guard now rejects it
        // earlier still, at this very step: House already has a parent (Electronic), and a parent must
        // always be top-level — so a 3-level chain, transitive cycle or not, can never be built at all.
        var hierarchy = new GenreHierarchy().SetParent("House", "Electronic");
        var mappingsBefore = new Dictionary<string, string>(hierarchy.Mappings);

        var ex = Assert.Throws<ArgumentException>(() => hierarchy.SetParent("Deep House", "House"));

        Assert.Contains("top-level", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(mappingsBefore, hierarchy.Mappings);
    }

    [Fact]
    public void SetParent_ToItself_IsRejected()
    {
        var hierarchy = new GenreHierarchy();

        Assert.Throws<ArgumentException>(() => hierarchy.SetParent("House", "House"));
        Assert.Null(hierarchy.ParentOf("House"));
    }

    // Phase 3 depth guard: exactly two levels, main genre -> sub-genre, never deeper.

    [Fact]
    public void SetParent_ParentThatIsItselfAChild_IsRejected_StateUnchanged()
    {
        // House is top-level, Deep House is its child. Deep House is therefore NOT a valid parent.
        var hierarchy = new GenreHierarchy().SetParent("Deep House", "House");
        var mappingsBefore = new Dictionary<string, string>(hierarchy.Mappings);

        var ex = Assert.Throws<ArgumentException>(() => hierarchy.SetParent("Tech House", "Deep House"));

        Assert.Contains("top-level", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(mappingsBefore, hierarchy.Mappings);
    }

    [Fact]
    public void SetParent_GenreThatAlreadyHasChildren_IsRejected_StateUnchanged()
    {
        // House already has a child (Deep House), so House can't become someone else's child too.
        var hierarchy = new GenreHierarchy().SetParent("Deep House", "House");
        var mappingsBefore = new Dictionary<string, string>(hierarchy.Mappings);

        var ex = Assert.Throws<ArgumentException>(() => hierarchy.SetParent("House", "Electronic"));

        Assert.Contains("sub-genres", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(mappingsBefore, hierarchy.Mappings);
    }

    [Fact]
    public void SetParent_LegitimateTwoLevelAssignment_StillSucceeds()
    {
        var hierarchy = new GenreHierarchy().SetParent("Tech House", "House");

        Assert.Equal("House", hierarchy.ParentOf("Tech House"));
        Assert.Equal(new[] { "Tech House" }, hierarchy.ChildrenOf("House"));
    }
}
