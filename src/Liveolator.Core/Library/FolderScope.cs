namespace Liveolator.Core.Library;

/// <summary>
/// Path-prefix helpers for deciding whether a file lives under a folder root. Matching is done on a
/// normalized form (forward slashes, no trailing separator) and only at a path boundary, so
/// "/music/rock" never absorbs "/music/rockabilly". Case-insensitive, mirroring the catalog comparer.
/// </summary>
public static class FolderScope
{
    /// <summary>Canonical form for comparison: forward slashes, no trailing separator.</summary>
    public static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');

    /// <summary>True when <paramref name="filePath"/> sits inside <paramref name="folderRoot"/> (both raw).</summary>
    public static bool IsUnder(string filePath, string folderRoot)
        => IsUnderNormalized(Normalize(filePath), Normalize(folderRoot));

    /// <summary>True when an already-normalized file path sits inside an already-normalized root.</summary>
    public static bool IsUnderNormalized(string filePath, string folderRoot)
        => folderRoot.Length > 0
           && filePath.StartsWith(folderRoot + "/", StringComparison.OrdinalIgnoreCase);
}
