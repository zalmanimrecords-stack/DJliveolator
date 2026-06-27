using Liveolator.Core.Library;
using Liveolator.Core.Library.Doctor;
using Xunit;

namespace Liveolator.Core.Tests.Library;

public class RelocationPlannerSmartTests
{
    private static readonly DateTime Old = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime New = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PlanSmart_MatchesHashBeforeNameSize()
    {
        var missing = new[]
        {
            new MissingMediaFile(new ScannedFile("/old/song.mp3", 100, Old), Sha256: "aaa"),
        };
        var candidates = new[]
        {
            new RelocationCandidate(new ScannedFile("/new/song.mp3", 100, New), Sha256: "bbb"),
            new RelocationCandidate(new ScannedFile("/new/renamed.mp3", 999, New), Sha256: "aaa"),
        };

        RelocationPlan plan = RelocationPlanner.PlanSmart(missing, candidates);

        RelocationMatch match = Assert.Single(plan.Matches);
        Assert.Equal("/new/renamed.mp3", match.NewFile.Path);
        Assert.Equal(LibraryRepairConfidence.High, match.Confidence);
    }

    [Fact]
    public void PlanSiblingFolder_MapsRelativePathsFromOneKnownPair()
    {
        var missing = new[]
        {
            new ScannedFile("/old/set/a.mp3", 10, Old),
            new ScannedFile("/old/set/sub/b.mp3", 20, Old),
        };
        var candidates = new[]
        {
            new ScannedFile("/new/set/a.mp3", 10, New),
            new ScannedFile("/new/set/sub/b.mp3", 20, New),
        };

        RelocationPlan plan = RelocationPlanner.PlanSiblingFolder(
            missing,
            "/old/set/a.mp3",
            new ScannedFile("/new/set/a.mp3", 10, New),
            candidates);

        Assert.Equal(2, plan.Matches.Count);
        Assert.All(plan.Matches, m => Assert.Equal(LibraryRepairConfidence.Medium, m.Confidence));
        Assert.Empty(plan.Unmatched);
    }
}

