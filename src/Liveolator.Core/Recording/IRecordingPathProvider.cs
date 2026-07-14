namespace Liveolator.Core.Recording;

/// <summary>
/// Supplies the destination path for a new master recording when the triggering action does not carry
/// an explicit one (roadmap X2). A seam so the recording handler stays pure (no filesystem/clock policy)
/// and unit-tests with a fixed path; the concrete provider (in the App composition root) names files by
/// timestamp under the user's recordings folder.
/// </summary>
public interface IRecordingPathProvider
{
    /// <summary>Returns a fresh, fully-qualified destination path for a recording starting now.</summary>
    string NextRecordingPath();
}
