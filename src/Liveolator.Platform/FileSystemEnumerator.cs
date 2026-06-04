using Liveolator.Core.Library;

namespace Liveolator.Platform;

/// <summary>
/// Real <see cref="IFileEnumerator"/> over the local filesystem (recursive, cross-platform
/// via System.IO). Unreadable files/folders are skipped rather than aborting the walk.
/// </summary>
public sealed class FileSystemEnumerator : IFileEnumerator
{
    public IEnumerable<ScannedFile> Enumerate(IReadOnlyList<string> folders, IReadOnlySet<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(extensions);

        foreach (string folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                continue;

            IEnumerable<string> paths;
            try
            {
                paths = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                // Folder became inaccessible mid-scan — skip it, don't crash the scan.
                continue;
            }

            foreach (string path in paths)
            {
                if (!extensions.Contains(Path.GetExtension(path)))
                    continue;

                ScannedFile? file = TryStat(path);
                if (file is not null)
                    yield return file.Value;
            }
        }
    }

    private static ScannedFile? TryStat(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return new ScannedFile(path, info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception)
        {
            return null; // permission/IO error on a single file — skip it.
        }
    }
}
