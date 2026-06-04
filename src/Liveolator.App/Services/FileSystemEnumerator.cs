using System.IO;
using Liveolator.Core.Library;

namespace Liveolator.App.Services;

/// <summary>
/// Real filesystem implementation of <see cref="IFileEnumerator"/> (the binding Core's
/// library logic depends on). Walks the given folders recursively and yields files whose
/// extension is in the requested set. Inaccessible folders/files are skipped rather than
/// aborting the whole scan.
/// </summary>
public sealed class FileSystemEnumerator : IFileEnumerator
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
    };

    public IEnumerable<ScannedFile> Enumerate(IReadOnlyList<string> folders, IReadOnlySet<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(extensions);

        foreach (string folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                continue;

            foreach (string path in Directory.EnumerateFiles(folder, "*", Options))
            {
                if (!extensions.Contains(Path.GetExtension(path)))
                    continue;

                ScannedFile? file = TryDescribe(path);
                if (file is not null)
                    yield return file.Value;
            }
        }
    }

    private static ScannedFile? TryDescribe(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return new ScannedFile(path, info.Length, info.LastWriteTimeUtc);
        }
        catch (IOException)
        {
            return null; // transient/locked file: skip, don't fail the scan
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
