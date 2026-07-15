using System;
using System.Linq;
using Liveolator.Core.Library;
using Xunit;

namespace Liveolator.Core.Tests.Library;

public class DuplicateFinderTests
{
    // Minimal IMediaEntry so the test exercises the generic finder without building a full MusicTrack.
    private sealed record FakeEntry(ScannedFile File, MediaAnalysisStatus Status = MediaAnalysisStatus.Ok)
        : IMediaEntry;

    private static FakeEntry Entry(string path, long sizeBytes)
        => new(new ScannedFile(path, sizeBytes, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

    [Fact]
    public void Find_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => DuplicateFinder.Find<FakeEntry>(null!));

    [Fact]
    public void NoDuplicates_ReturnsEmpty()
    {
        var entries = new[]
        {
            Entry(@"C:\music\a.mp3", 1000),
            Entry(@"C:\music\b.mp3", 2000),
        };

        Assert.Empty(DuplicateFinder.Find(entries));
    }

    [Fact]
    public void SameNameAndSize_InDifferentFolders_IsOneGroup()
    {
        var entries = new[]
        {
            Entry(@"C:\music\song.mp3", 5_000_000),
            Entry(@"D:\backup\song.mp3", 5_000_000),
        };

        DuplicateGroup<FakeEntry> group = Assert.Single(DuplicateFinder.Find(entries));
        Assert.Equal(
            new[] { @"C:\music\song.mp3", @"D:\backup\song.mp3" },
            group.Entries.Select(e => e.File.Path));
    }

    [Fact]
    public void SameName_DifferentSize_IsNotADuplicate()
    {
        // A re-encode (different byte size) is a different file by this heuristic — not flagged.
        var entries = new[]
        {
            Entry(@"C:\music\song.mp3", 5_000_000),
            Entry(@"D:\backup\song.mp3", 4_000_000),
        };

        Assert.Empty(DuplicateFinder.Find(entries));
    }

    [Fact]
    public void SameSize_DifferentName_IsNotADuplicate()
    {
        var entries = new[]
        {
            Entry(@"C:\music\one.mp3", 5_000_000),
            Entry(@"C:\music\two.mp3", 5_000_000),
        };

        Assert.Empty(DuplicateFinder.Find(entries));
    }

    [Fact]
    public void FileNameMatch_IsCaseInsensitive()
    {
        var entries = new[]
        {
            Entry(@"C:\music\Song.MP3", 5_000_000),
            Entry(@"D:\backup\song.mp3", 5_000_000),
        };

        Assert.Single(DuplicateFinder.Find(entries));
    }

    [Fact]
    public void ThreeCopies_AreOneGroupOfThree()
    {
        var entries = new[]
        {
            Entry(@"C:\a\song.mp3", 5_000_000),
            Entry(@"C:\b\song.mp3", 5_000_000),
            Entry(@"C:\c\song.mp3", 5_000_000),
        };

        DuplicateGroup<FakeEntry> group = Assert.Single(DuplicateFinder.Find(entries));
        Assert.Equal(3, group.Entries.Count);
    }

    [Fact]
    public void SamePathRepeated_IsNotADuplicate()
    {
        // An accidental repeat of the same path in the input is not two files.
        var entries = new[]
        {
            Entry(@"C:\music\song.mp3", 5_000_000),
            Entry(@"C:\music\song.mp3", 5_000_000),
        };

        Assert.Empty(DuplicateFinder.Find(entries));
    }

    [Fact]
    public void MultipleGroups_AreOrderedDeterministically_BySizeThenName()
    {
        var entries = new[]
        {
            Entry(@"C:\x\zeta.mp3", 9_000),
            Entry(@"C:\y\zeta.mp3", 9_000),
            Entry(@"C:\x\alpha.mp3", 1_000),
            Entry(@"C:\y\alpha.mp3", 1_000),
        };

        var groups = DuplicateFinder.Find(entries);

        Assert.Equal(2, groups.Count);
        // Smaller size first ("alpha" at 1000), then the larger ("zeta" at 9000). Use the portable
        // file-name helper (not System.IO.Path) so the assertion holds on macOS too, where Path would
        // not split the Windows fixture path.
        Assert.Equal("alpha.mp3", PortablePath.GetFileName(groups[0].Entries[0].File.Path));
        Assert.Equal("zeta.mp3", PortablePath.GetFileName(groups[1].Entries[0].File.Path));
    }
}
