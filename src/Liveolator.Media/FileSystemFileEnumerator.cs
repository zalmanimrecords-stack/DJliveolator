using Liveolator.Core.Library;

namespace Liveolator.Media;

/// <summary>
/// Real filesystem implementation of the <see cref="IFileEnumerator"/> seam: walks the given
/// folders recursively and yields files whose extension is in the requested set. Unreadable
/// directories are skipped (a missing/denied folder must not abort a whole library scan).
/// </summary>
public sealed class FileSystemFileEnumerator : IFileEnumerator
{
    public IEnumerable<ScannedFile> Enumerate(IReadOnlyList<string> folders, IReadOnlySet<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(extensions);

        foreach (string folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                continue;

            foreach (ScannedFile file in EnumerateFolder(folder, extensions))
                yield return file;
        }
    }

    private static IEnumerable<ScannedFile> EnumerateFolder(string folder, IReadOnlySet<string> extensions)
    {
        // EnumerationOptions skips files/dirs we can't read instead of throwing mid-walk.
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System,
        };

        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(folder, "*", options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break; // folder vanished or became unreadable between Exists and enumerate
        }

        foreach (string path in paths)
        {
            if (!extensions.Contains(Path.GetExtension(path)))
                continue;

            ScannedFile? file = TryScan(path);
            if (file.HasValue)
                yield return file.Value;
        }
    }

    private static ScannedFile? TryScan(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return new ScannedFile(path, info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // file disappeared or is locked — drop it from this scan
        }
    }
}
