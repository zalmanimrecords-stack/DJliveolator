using Liveolator.Core.Library;
using Xunit;

namespace Liveolator.Core.Tests.Library;

public class IncrementalScanTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Diff_ClassifiesAddedModifiedUnchangedRemoved()
    {
        var unchanged = new ScannedFile("keep.mp3", 100, T);
        var modified = new ScannedFile("change.mp3", 250, T);   // size differs from known
        var added = new ScannedFile("new.mp3", 300, T);

        var known = new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase)
        {
            ["keep.mp3"] = FileFingerprint.Of(unchanged),
            ["change.mp3"] = new FileFingerprint(200, T),
            ["gone.mp3"] = new FileFingerprint(400, T),
        };

        var deltas = IncrementalScan.Diff(new[] { unchanged, modified, added }, known);

        Assert.Contains(deltas, d => d.Change == ScanChange.Unchanged && d.File.Path == "keep.mp3");
        Assert.Contains(deltas, d => d.Change == ScanChange.Modified && d.File.Path == "change.mp3");
        Assert.Contains(deltas, d => d.Change == ScanChange.Added && d.File.Path == "new.mp3");
        Assert.Contains(deltas, d => d.Change == ScanChange.Removed && d.File.Path == "gone.mp3");
    }

    [Fact]
    public void Diff_EmptyKnown_AllAdded()
    {
        var files = new[] { new ScannedFile("a.mp3", 1, T), new ScannedFile("b.mp3", 2, T) };
        var deltas = IncrementalScan.Diff(files, new Dictionary<string, FileFingerprint>());
        Assert.All(deltas, d => Assert.Equal(ScanChange.Added, d.Change));
    }
}
