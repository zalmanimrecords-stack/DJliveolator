using Liveolator.Core.Library;

namespace Liveolator.App.Tests.Fakes;

/// <summary>Returns a fixed file set regardless of folders, so library tests never touch disk.</summary>
public sealed class FakeFileEnumerator : IFileEnumerator
{
    private readonly IReadOnlyList<ScannedFile> _files;

    public FakeFileEnumerator(params string[] paths)
        => _files = paths
            .Select(p => new ScannedFile(p, 1000, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)))
            .ToList();

    public IEnumerable<ScannedFile> Enumerate(IReadOnlyList<string> folders, IReadOnlySet<string> extensions)
        => _files;
}
