namespace Liveolator.Core.Library;

/// <summary>
/// Reports whether a catalogued file is currently reachable on disk. A Core seam (no platform IO
/// lives in Core): the OS-backed implementation lives in Liveolator.Platform and is wired at
/// composition. Used to find missing entries — e.g. tracks on a network drive that goes offline —
/// so the user can relocate them in bulk without losing analysis.
/// </summary>
public interface IFileExistenceProbe
{
    /// <summary>
    /// Returns <c>true</c> when a file exists at <paramref name="path"/>, <c>false</c> otherwise.
    /// Must never throw (an unreachable drive or a permission error is reported as "does not exist"),
    /// so a single bad path can never abort a bulk check.
    /// </summary>
    bool Exists(string path);
}
