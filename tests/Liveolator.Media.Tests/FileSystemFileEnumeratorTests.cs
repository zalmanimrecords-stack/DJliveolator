using Liveolator.Core.Library;
using Xunit;

namespace Liveolator.Media.Tests;

public class FileSystemFileEnumeratorTests
{
    private static readonly IReadOnlySet<string> AudioExt =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3" };

    private readonly FileSystemFileEnumerator _enumerator = new();

    [Fact]
    public void Enumerate_ReturnsOnlyMatchingExtensions_Recursively()
    {
        using var dir = new TempDirectory();
        dir.Touch("a.wav");
        dir.Touch("b.mp3");
        dir.Touch("notes.txt");          // wrong extension
        dir.Touch("sub/c.wav");          // nested

        var found = _enumerator.Enumerate(new[] { dir.Path }, AudioExt).ToList();

        Assert.Equal(3, found.Count);
        Assert.All(found, f => Assert.Contains(Path.GetExtension(f.Path), AudioExt));
        Assert.Contains(found, f => f.Path.EndsWith("c.wav", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Enumerate_PopulatesSizeAndModifiedTime()
    {
        using var dir = new TempDirectory();
        string path = dir.Touch("a.wav", content: "1234567890"); // 10 bytes

        ScannedFile file = _enumerator.Enumerate(new[] { dir.Path }, AudioExt).Single();

        Assert.Equal(10, file.SizeBytes);
        Assert.Equal(DateTimeKind.Utc, file.LastModifiedUtc.Kind);
    }

    [Fact]
    public void Enumerate_EmitsCanonicalFullPaths_RegardlessOfFolderSpelling()
    {
        using var dir = new TempDirectory();
        dir.Touch("a.wav");
        // A folder argument with a redundant "." segment must still yield a canonical key,
        // so the same file never gets two spellings in the catalog (incremental-cache stability).
        string quirky = Path.Combine(dir.Path, ".");

        ScannedFile file = _enumerator.Enumerate(new[] { quirky }, AudioExt).Single();

        Assert.True(Path.IsPathFullyQualified(file.Path));
        Assert.Equal(Path.GetFullPath(file.Path), file.Path);
    }

    [Fact]
    public void Enumerate_SkipsMissingFolders_WithoutThrowing()
    {
        using var dir = new TempDirectory();
        dir.Touch("a.wav");
        string missing = Path.Combine(dir.Path, "does-not-exist");

        var found = _enumerator.Enumerate(new[] { missing, dir.Path }, AudioExt).ToList();

        Assert.Single(found);
    }
}
