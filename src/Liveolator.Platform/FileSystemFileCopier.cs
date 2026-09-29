using Liveolator.Core.Library;

namespace Liveolator.Platform;

/// <summary>
/// Real <see cref="IFileCopier"/> over the local filesystem (cross-platform via System.IO). Copies into a
/// <c>.partial</c> sibling and renames it into place only once the byte count matches, so a dropped NAS
/// link or a cancel never leaves a truncated track that a later run would take for a finished one.
/// </summary>
public sealed class FileSystemFileCopier : IFileCopier
{
    private const int BufferBytes = 1 << 20;

    public async Task<ScannedFile> CopyAsync(
        string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        string partial = destinationPath + ".partial";
        try
        {
            long expected;
            await using (var source = new FileStream(
                sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, useAsync: true))
            await using (var target = new FileStream(
                partial, FileMode.Create, FileAccess.Write, FileShare.None, BufferBytes, useAsync: true))
            {
                expected = source.Length;
                await source.CopyToAsync(target, BufferBytes, cancellationToken).ConfigureAwait(false);
            }

            long actual = new FileInfo(partial).Length;
            if (actual != expected)
                throw new IOException($"Copied {actual} of {expected} bytes of {sourcePath}.");
            File.Move(partial, destinationPath, overwrite: true);
        }
        catch
        {
            File.Delete(partial); // no-op when it was never created; the original failure is rethrown
            throw;
        }

        return TryStat(destinationPath)
            ?? throw new IOException($"{destinationPath} vanished right after it was copied.");
    }

    public ScannedFile? TryStat(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? new ScannedFile(path, info.Length, info.LastWriteTimeUtc) : null;
    }

    public long AvailableFreeBytes(string folder)
    {
        string full = Path.GetFullPath(folder);
        // The longest matching mount wins, so a USB stick under /Volumes on macOS is not read as "/".
        DriveInfo drive = DriveInfo.GetDrives()
            .Where(d => full.StartsWith(d.Name, StringComparison.OrdinalIgnoreCase))
            .MaxBy(d => d.Name.Length)
            ?? throw new IOException($"Cannot tell how much space is free at {folder}. Choose a folder on a local disk.");
        return drive.AvailableFreeSpace;
    }
}
