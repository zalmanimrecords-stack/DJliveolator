using Liveolator.Core.Library;
using Liveolator.Platform;
using Xunit;

namespace Liveolator.Integration.Tests;

public class FileSystemFileCopierTests
{
    private readonly FileSystemFileCopier _copier = new();

    [Fact]
    public async Task Copy_CreatesTheFolders_AndReturnsTheCopiedFilesStat()
    {
        using var dir = new TempDir();
        string source = dir.Write("nas/a.mp3", new byte[] { 1, 2, 3 });
        string destination = Path.Combine(dir.Path, "gig", "music", "sub", "a.mp3");

        ScannedFile copied = await _copier.CopyAsync(source, destination);

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(destination));
        Assert.Equal(destination, copied.Path);
        Assert.Equal(3, copied.SizeBytes);
        Assert.Equal(File.GetLastWriteTimeUtc(destination), copied.LastModifiedUtc);
        Assert.False(File.Exists(destination + ".partial"));
    }

    [Fact]
    public async Task Copy_ReplacesAStaleFileAtTheDestination()
    {
        using var dir = new TempDir();
        string source = dir.Write("nas/a.mp3", new byte[] { 1, 2, 3 });
        string destination = dir.Write("gig/a.mp3", new byte[] { 9 });

        await _copier.CopyAsync(source, destination);

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(destination));
    }

    [Fact]
    public async Task Copy_Cancelled_LeavesNoPartialAndNoDestination()
    {
        using var dir = new TempDir();
        string source = dir.Write("nas/a.mp3", new byte[1 << 20]);
        string destination = Path.Combine(dir.Path, "gig", "a.mp3");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _copier.CopyAsync(source, destination, new CancellationToken(canceled: true)));

        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".partial"));
    }

    [Fact]
    public async Task Copy_MissingSource_Throws_AndLeavesNothingBehind()
    {
        using var dir = new TempDir();
        string destination = Path.Combine(dir.Path, "gig", "a.mp3");

        await Assert.ThrowsAnyAsync<IOException>(
            () => _copier.CopyAsync(Path.Combine(dir.Path, "missing.mp3"), destination));

        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".partial"));
    }

    [Fact]
    public void TryStat_IsNullForAMissingFile_AndTheStatForAPresentOne()
    {
        using var dir = new TempDir();
        string present = dir.Write("a.mp3", new byte[] { 1, 2 });

        Assert.Null(_copier.TryStat(Path.Combine(dir.Path, "missing.mp3")));
        Assert.Equal(2, _copier.TryStat(present)!.Value.SizeBytes);
    }

    [Fact]
    public void AvailableFreeBytes_WorksForAFolderThatDoesNotExistYet()
    {
        using var dir = new TempDir();

        Assert.True(_copier.AvailableFreeBytes(Path.Combine(dir.Path, "not", "yet")) > 0);
    }
}
