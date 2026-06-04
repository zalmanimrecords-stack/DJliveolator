using Liveolator.Platform;
using Xunit;

namespace Liveolator.Integration.Tests;

public class FileSystemEnumeratorTests
{
    private static readonly HashSet<string> AudioExt =
        new(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3" };

    [Fact]
    public void Enumerate_ReturnsMatchingExtensions_Recursively_WithStats()
    {
        using var dir = new TempDir();
        dir.Write("a.wav", new byte[] { 1, 2, 3 });
        dir.Write("b.mp3", new byte[] { 1 });
        dir.Write("notes.txt", new byte[] { 9 });
        dir.Write("sub/c.wav", new byte[] { 1, 2, 3, 4, 5 });

        var files = new FileSystemEnumerator()
            .Enumerate(new[] { dir.Path }, AudioExt)
            .ToList();

        Assert.Equal(3, files.Count); // a.wav, b.mp3, sub/c.wav — not notes.txt
        Assert.Contains(files, f => f.Path.EndsWith("a.wav") && f.SizeBytes == 3);
        Assert.Contains(files, f => f.Path.EndsWith("c.wav") && f.SizeBytes == 5);
        Assert.DoesNotContain(files, f => f.Path.EndsWith("notes.txt"));
    }

    [Fact]
    public void Enumerate_MissingFolder_IsSkipped()
    {
        var files = new FileSystemEnumerator()
            .Enumerate(new[] { Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid()) }, AudioExt)
            .ToList();
        Assert.Empty(files);
    }
}
