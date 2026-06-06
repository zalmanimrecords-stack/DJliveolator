using Liveolator.Core.Library;
using Liveolator.Core.Library.Visual;
using Xunit;

namespace Liveolator.Core.Tests.Library;

public class VisualAssetQueryTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static VisualAsset Asset(
        string path, VisualMediaKind kind, MediaAnalysisStatus status = MediaAnalysisStatus.Ok)
        => new(
            new ScannedFile(path, 1000, T), kind,
            kind == VisualMediaKind.Video
                ? new VisualMediaInfo(1920, 1080, TimeSpan.FromSeconds(10))
                : new VisualMediaInfo(800, 600, null),
            status, status == MediaAnalysisStatus.Failed ? "boom" : null);

    private static readonly VisualAsset[] Catalog =
    {
        Asset("/vis/sunset.jpg", VisualMediaKind.Image),
        Asset("/vis/loop.mp4", VisualMediaKind.Video),
        Asset("/vis/grid.png", VisualMediaKind.Image),
        Asset("/vis/broken.gif", VisualMediaKind.Image, MediaAnalysisStatus.Failed),
    };

    [Fact]
    public void Empty_filter_returns_all_ordered_by_title()
    {
        IReadOnlyList<VisualAsset> result = VisualAssetQuery.Apply(Catalog, new VisualAssetFilter());

        Assert.Equal(4, result.Count);
        // Title is the file name without extension: broken, grid, loop, sunset.
        Assert.Equal(new[] { "broken", "grid", "loop", "sunset" }, result.Select(a => a.Title));
    }

    [Fact]
    public void Kind_facet_filters_to_videos()
    {
        IReadOnlyList<VisualAsset> result =
            VisualAssetQuery.Apply(Catalog, new VisualAssetFilter(Kind: VisualMediaKind.Video));

        VisualAsset only = Assert.Single(result);
        Assert.Equal("loop", only.Title);
    }

    [Fact]
    public void Status_facet_filters_to_failed()
    {
        IReadOnlyList<VisualAsset> result =
            VisualAssetQuery.Apply(Catalog, new VisualAssetFilter(Status: MediaAnalysisStatus.Failed));

        Assert.Equal("broken", Assert.Single(result).Title);
    }

    [Fact]
    public void Text_matches_title_and_file_name_case_insensitively()
    {
        Assert.Equal("sunset", Assert.Single(VisualAssetQuery.Apply(Catalog, new VisualAssetFilter(Text: "SUN"))).Title);
        Assert.Equal("loop", Assert.Single(VisualAssetQuery.Apply(Catalog, new VisualAssetFilter(Text: ".mp4"))).Title);
    }

    [Fact]
    public void Facets_compose_with_logical_and()
    {
        // Image ∧ "g" matches grid only (sunset/broken contain no 'g' in title or file name except grid).
        IReadOnlyList<VisualAsset> result = VisualAssetQuery.Apply(
            Catalog, new VisualAssetFilter(Kind: VisualMediaKind.Image, Text: "grid"));

        Assert.Equal("grid", Assert.Single(result).Title);
    }

    [Fact]
    public void Blank_text_matches_everything()
        => Assert.Equal(4, VisualAssetQuery.Apply(Catalog, new VisualAssetFilter(Text: "   ")).Count);

    [Fact]
    public void Limit_is_clamped_to_at_least_one()
        => Assert.Single(VisualAssetQuery.Apply(Catalog, new VisualAssetFilter(), limit: 1));

    [Fact]
    public void Null_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(() => VisualAssetQuery.Apply(null!, new VisualAssetFilter()));
        Assert.Throws<ArgumentNullException>(() => VisualAssetQuery.Apply(Catalog, null!));
    }
}
