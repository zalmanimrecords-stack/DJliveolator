using Liveolator.Core.Library;

namespace Liveolator.Platform;

/// <summary>
/// Real <see cref="IFileRemover"/> over the local filesystem (cross-platform via System.IO).
/// Permanently deletes the file; a missing path or an OS error surfaces as an exception so the
/// caller can report it (never a silent failure).
/// </summary>
public sealed class FileSystemFileRemover : IFileRemover
{
    public void Delete(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("File path is required.", nameof(path));

        // File.Delete is a no-op for a path that does not exist; treat the asset as already gone
        // rather than erroring, so deleting a catalog entry whose file was removed out-of-band still
        // cleans up the catalog. Real IO/permission errors propagate.
        File.Delete(path);
    }
}
