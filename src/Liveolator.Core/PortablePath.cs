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
}
