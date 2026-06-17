using Liveolator.Core.Recording;

namespace Liveolator.App.Composition;

/// <summary>
/// Names each master recording (roadmap X2) by start time under a "recordings" folder beside the app's
/// other persisted state, so captures land in a predictable place and never collide. The directory is
/// created lazily on first use; if creation fails the path is still returned and the recorder surfaces
/// the open failure (tolerant - a recording must not crash the app).
/// </summary>
public sealed class TimestampedRecordingPathProvider : IRecordingPathProvider
{
    private readonly string _directory;

    public TimestampedRecordingPathProvider(string persistenceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(persistenceRoot);
        _directory = Path.Combine(persistenceRoot, "recordings");
    }

    public string NextRecordingPath()
    {
        try
        {
            Directory.CreateDirectory(_directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave directory creation to the recorder's open attempt, which logs on failure.
            System.Diagnostics.Trace.TraceWarning($"Could not create recordings directory: {ex.Message}");
        }

        string name = $"master-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.wav";
        return Path.Combine(_directory, name);
    }
}
