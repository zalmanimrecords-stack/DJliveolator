using Liveolator.Core.Library.Visual;

namespace Liveolator.App.Tests.Fakes;

/// <summary>
/// Deterministic <see cref="IVisualMediaProbe"/> for view-model tests: returns fixed dimensions and,
/// for videos, a fixed duration — so a scan never touches a real decoder or disk. Paths listed in
/// <see cref="FailPaths"/> throw, exercising the library's per-file failure isolation.
/// </summary>
public sealed class FakeVisualMediaProbe : IVisualMediaProbe
{
    public HashSet<string> FailPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<VisualMediaInfo> ProbeAsync(
        string filePath, VisualMediaKind kind, CancellationToken cancellationToken = default)
    {
        if (FailPaths.Contains(filePath))
            throw new InvalidOperationException($"probe failed for {filePath}");

        VisualMediaInfo info = kind == VisualMediaKind.Video
            ? new VisualMediaInfo(1920, 1080, TimeSpan.FromSeconds(12))
            : new VisualMediaInfo(800, 600, null);
        return Task.FromResult(info);
    }
}
