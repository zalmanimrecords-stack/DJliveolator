using System;
using System.Text;

namespace Liveolator.Core;

/// <summary>
/// OS-independent file-name helpers for paths that may have been authored on a DIFFERENT platform than
/// the one running now. Liveolator runs on Windows AND macOS (a hard requirement), and the catalog stores
/// whatever path a track was scanned under — a Windows drive path (<c>C:\music\x.mp3</c>), a UNC share
/// (<c>\\host\share\x.mp3</c>), or a Unix path (<c>/Users/x.mp3</c>). <see cref="System.IO.Path"/> only
/// recognises the HOST OS separator, so on macOS <c>Path.GetFileName("C:\\a\\b.mp3")</c> returns the whole
/// string — silently breaking file-name matching, duplicate detection, and title fallbacks for a catalog
/// synced from Windows. These split on BOTH <c>'/'</c> and <c>'\\'</c> regardless of host, and are
/// byte-identical to <see cref="System.IO.Path"/> for native paths on Windows.
/// </summary>
public static class PortablePath
{
    private static readonly char[] Separators = { '/', '\\' };

    /// <summary>
    /// The final path segment (file name with extension), splitting on both <c>'/'</c> and <c>'\\'</c>.
    /// Returns the input unchanged when it has no separator, and an empty string for a trailing separator
    /// — mirroring <see cref="System.IO.Path.GetFileName(string)"/> but separator-agnostic.
    /// </summary>
    public static string GetFileName(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        int cut = path.LastIndexOfAny(Separators);
        return cut < 0 ? path : path[(cut + 1)..];
    }

    /// <summary>
    /// The file name without its final extension, honouring both separators. Matches
    /// <see cref="System.IO.Path.GetFileNameWithoutExtension(string)"/> for native paths.
    /// </summary>
    public static string GetFileNameWithoutExtension(string? path)
    {
        string name = GetFileName(path);
        int dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[..dot];
    }

    /// <summary>
    /// Re-roots <paramref name="path"/> from one share prefix to another, e.g. the Linux mount point a
    /// server catalogued a track under (<c>/media/simon/external_4tb/x.mp3</c>) to the UNC share the same
    /// file is reached by here (<c>\host\Storage\x.mp3</c>). Returns <c>null</c> when the path does not
    /// start with <paramref name="fromPrefix"/> — that row belongs to some other root and is not ours to
    /// translate, which is a normal outcome, not an error.
    /// </summary>
    /// <remarks>
    /// Prefix matching is case-insensitive because the two sides are authored on different platforms and a
    /// user typing the share root will not match a Linux mount's casing by luck. The separators of the
    /// REMAINDER are rewritten to whichever separator the target root uses, so the result is a path the
    /// target platform actually accepts rather than a mixed-separator hybrid.
    /// </remarks>
    public static string? Rebase(string? path, string? fromPrefix, string? toPrefix)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(fromPrefix) || toPrefix is null)
            return null;

        string from = TrimTrailingSeparators(fromPrefix!);
        string to = TrimTrailingSeparators(toPrefix);
        if (from.Length == 0 || !path!.StartsWith(from, StringComparison.OrdinalIgnoreCase))
            return null;

        string rest = path[from.Length..];
        // "/media/x" must not match the prefix "/media/xylophone": only a separator (or the end of the
        // path) may follow, or a sibling directory sharing a name prefix is silently rebased.
        if (rest.Length > 0 && Array.IndexOf(Separators, rest[0]) < 0)
            return null;

        char target = to.StartsWith(Separators[0]) ? Separators[0] : Separators[1];
        var rebased = new StringBuilder(to, to.Length + rest.Length);
        foreach (char c in rest)
            rebased.Append(Array.IndexOf(Separators, c) >= 0 ? target : c);
        return rebased.ToString();
    }

    private static string TrimTrailingSeparators(string value)
    {
        int end = value.Length;
        while (end > 0 && Array.IndexOf(Separators, value[end - 1]) >= 0)
            end--;
        return value[..end];
    }
}
