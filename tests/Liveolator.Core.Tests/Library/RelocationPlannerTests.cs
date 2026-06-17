using Liveolator.Core.Library;
using Xunit;

namespace Liveolator.Core.Tests.Library;

public class RelocationPlannerTests
{
    private static readonly DateTime Old = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime New = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ScannedFile Missing(string path, long size = 1000) => new(path, size, Old);

    [Fact]
    public void Plan_MatchesByFileNameAndSize_IgnoringFolderAndModifiedTime()
    {
        var missing = new[] { Missing(@"S:\music\song.mp3", 1000) };
        var candidates = new[] { new ScannedFile(@"D:\local\song.mp3", 1000, New) };

        RelocationPlan plan = RelocationPlanner.Plan(missing, candidates);

        RelocationMatch match = Assert.Single(plan.Matches);
        Assert.Equal(@"S:\music\song.mp3", match.OldPath);
        Assert.Equal(@"D:\local\song.mp3", match.NewFile.Path);
        // The new fingerprint is carried so the catalog records the relocated file as-is.
        Assert.Equal(New, match.NewFile.LastModifiedUtc);
        Assert.Empty(plan.Unmatched);
    }

    [Fact]
    public void Plan_FileNameMatchIsCaseInsensitive()
    {
        var missing = new[] { Missing(@"S:\music\Song.MP3", 1000) };
        var candidates = new[] { new ScannedFile(@"D:\local\song.mp3", 1000, New) };

        RelocationPlan plan = RelocationPlanner.Plan(missing, candidates);

        Assert.Single(plan.Matches);
        Assert.Empty(plan.Unmatched);
    }

    [Fact]
    public void Plan_DoesNotMatchWhenSizeDiffers()
    {
        var missing = new[] { Missing(@"S:\music\song.mp3", 1000) };
        var candidates = new[] { new ScannedFile(@"D:\local\song.mp3", 2000, New) };

        RelocationPlan plan = RelocationPlanner.Plan(missing, candidates);

        Assert.Empty(plan.Matches);
        ScannedFile unmatched = Assert.Single(plan.Unmatched);
        Assert.Equal(@"S:\music\song.mp3", unmatched.Path);
    }

    [Fact]
    public void Plan_ReportsUnmatchedWhenNoCandidate()
    {
        var missing = new[] { Missing(@"S:\music\a.mp3"), Missing(@"S:\music\b.mp3") };
        var candidates = new[] { new ScannedFile(@"D:\local\a.mp3", 1000, New) };

        RelocationPlan plan = RelocationPlanner.Plan(missing, candidates);

        Assert.Single(plan.Matches);
        ScannedFile unmatched = Assert.Single(plan.Unmatched);
        Assert.Equal(@"S:\music\b.mp3", unmatched.Path);
    }

    [Fact]
    public void Plan_EachCandidateMatchesAtMostOneMissingEntry()
    {
        // Two missing files share the same identity (same name + size); only one candidate exists.
        var missing = new[] { Missing(@"S:\one\song.mp3", 1000), Missing(@"S:\two\song.mp3", 1000) };
        var candidates = new[] { new ScannedFile(@"D:\local\song.mp3", 1000, New) };

        RelocationPlan plan = RelocationPlanner.Plan(missing, candidates);

        Assert.Single(plan.Matches);
        Assert.Single(plan.Unmatched);
    }

    [Fact]
    public void Plan_DoesNotReuseACandidateForADifferentIdentity()
    {
        var missing = new[] { Missing(@"S:\music\song.mp3", 1000) };
        var candidates = new[]
        {
            new ScannedFile(@"D:\local\other.mp3", 1000, New),
            new ScannedFile(@"D:\local\song.mp3", 1000, New),
        };

        RelocationPlan plan = RelocationPlanner.Plan(missing, candidates);

        RelocationMatch match = Assert.Single(plan.Matches);
        Assert.Equal(@"D:\local\song.mp3", match.NewFile.Path);
    }

    [Fact]
    public void Plan_EmptyInputs_YieldEmptyPlan()
    {
        RelocationPlan plan = RelocationPlanner.Plan(
            Array.Empty<ScannedFile>(), Array.Empty<ScannedFile>());

        Assert.Empty(plan.Matches);
        Assert.Empty(plan.Unmatched);
    }

    [Fact]
    public void Plan_NullMissing_Throws()
        => Assert.Throws<ArgumentNullException>(
            () => RelocationPlanner.Plan(null!, Array.Empty<ScannedFile>()));

    [Fact]
    public void Plan_NullCandidates_Throws()
        => Assert.Throws<ArgumentNullException>(
            () => RelocationPlanner.Plan(Array.Empty<ScannedFile>(), null!));
}
