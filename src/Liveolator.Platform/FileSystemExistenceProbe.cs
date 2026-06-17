using Liveolator.Core.Library;

namespace Liveolator.Platform;

/// <summary>
/// Real <see cref="IFileExistenceProbe"/> over the local filesystem (cross-platform via System.IO).
/// An unreachable drive, a permission error, or any IO fault is treated as "does not exist" rather
/// than thrown, so a bulk missing-file scan never aborts on one bad path.
/// </summary>
public sealed class FileSystemExistenceProbe : IFileExistenceProbe
{
    public bool Exists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            return File.Exists(path);
        }
        catch (Exception)
        {
            // A torn network mount can surface an IO/permission exception from File.Exists; for a
            // missing-file scan that is indistinguishable from "gone", so report it as not present.
            return false;
        }
    }
}
