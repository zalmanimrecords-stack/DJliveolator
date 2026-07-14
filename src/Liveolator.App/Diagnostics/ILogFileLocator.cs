namespace Liveolator.App.Diagnostics;

/// <summary>
/// Seam that exposes where the log file lives and opens it in the OS file manager. Lets the Settings
/// view-model surface the log "link" without taking a dependency on native shell APIs (keeping the
/// view-model unit-testable with a fake).
/// </summary>
public interface ILogFileLocator
{
    /// <summary>The folder that holds the log files.</summary>
    string Directory { get; }

    /// <summary>The absolute path of the active log file.</summary>
    string CurrentFilePath { get; }

    /// <summary>Opens the log folder in the platform file manager. Best-effort: never throws.</summary>
    void RevealInFileManager();
}
