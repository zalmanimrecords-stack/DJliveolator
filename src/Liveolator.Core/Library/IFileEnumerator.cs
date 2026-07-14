namespace Liveolator.Core.Library;

/// <summary>
/// Enumerates candidate media files under folders, filtered by extension. The real
/// filesystem implementation lives in a binding project; Core depends only on this seam so
/// library logic unit-tests without touching disk.
/// </summary>
public interface IFileEnumerator
{
    /// <summary>
    /// Returns files under <paramref name="folders"/> whose extension is in
    /// <paramref name="extensions"/> (case-insensitive, leading-dot form, e.g. ".mp3").
    /// </summary>
    IEnumerable<ScannedFile> Enumerate(IReadOnlyList<string> folders, IReadOnlySet<string> extensions);
}
