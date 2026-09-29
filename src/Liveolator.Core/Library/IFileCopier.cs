namespace Liveolator.Core.Library;

/// <summary>
/// Copies files and inspects the destination side. A Core seam (no platform IO lives in Core): the
/// OS-backed implementation lives in Liveolator.Platform and is wired at composition. Used to take a
/// playlist offline onto the laptop before a gig.
/// </summary>
public interface IFileCopier
{
    /// <summary>
    /// Copies <paramref name="sourcePath"/> to <paramref name="destinationPath"/>, creating folders and
    /// replacing any file already there, and returns the COPIED file's own size and timestamp (a copy does
    /// not reliably keep the source's). Never leaves a partial file at the destination, including on
    /// cancellation; throws on failure so the caller can report it.
    /// </summary>
    Task<ScannedFile> CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>The file's size and timestamp, or null when nothing is there.</summary>
    ScannedFile? TryStat(string path);

    /// <summary>Free bytes on the volume that holds <paramref name="folder"/>, which need not exist yet.</summary>
    long AvailableFreeBytes(string folder);
}
