using Liveolator.Core.Analysis.Structure;
using Xunit;

namespace Liveolator.Core.Tests.Analysis.Structure;

public class SongStructureTests
{
    [Fact]
    public void Section_HoldsStartAndLabel()
    {
        var s = new SongSection(12.5, SongSectionLabel.Drop);
        Assert.Equal(12.5, s.StartSeconds);
        Assert.Equal("drop", s.Label);
    }

    [Fact]
    public void Ordered_SortsSectionsByStartTime()
    {
        var structure = new SongStructure(
            new[]
            {
                new SongSection(30.0, SongSectionLabel.Drop),
                new SongSection(0.0, SongSectionLabel.Intro),
                new SongSection(10.0, SongSectionLabel.BuildUp),
            },
            "librosa 0.10.2");

        Assert.Equal(new[] { 0.0, 10.0, 30.0 }, structure.Ordered.Select(s => s.StartSeconds));
        Assert.Equal("librosa 0.10.2", structure.AnalyzedWith);
    }

    [Fact]
    public void Records_HaveValueEquality()
    {
        var a = new SongStructure(new[] { new SongSection(1.0, "drop") }, "librosa 0.10");
        var b = new SongStructure(new[] { new SongSection(1.0, "drop") }, "librosa 0.10");
        Assert.Equal(a.Sections[0], b.Sections[0]);
    }
}
