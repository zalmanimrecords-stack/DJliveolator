namespace Liveolator.Core.Library;

/// <summary>
/// Deletes a file from the underlying storage. A Core seam (no platform IO lives in Core):
/// the OS-backed implementation lives in Liveolator.Platform and is wired at composition.
/// Used by the library tabs to remove an asset's file from disk when the user deletes it.
/// </summary>
public interface IFileRemover
{
    /// <summary>
    /// Permanently deletes the file at <paramref name="path"/>. Throws on failure (missing file,
    /// permission/IO error) so the caller can surface it; never fails silently.
    /// </summary>
    void Delete(string path);
}
