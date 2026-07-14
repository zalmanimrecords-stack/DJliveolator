using Microsoft.Extensions.Logging;

namespace Liveolator.App.Diagnostics;

/// <summary>
/// Configuration for the on-disk log sink (<see cref="FileLoggerProvider"/>). Pure data; the directory
/// and verbosity come from the app host / persisted <c>DiagnosticsSettings</c>, the rotation limits are
/// sensible defaults that keep the log bounded on a performer's machine.
/// </summary>
public sealed class FileLoggerOptions
{
    /// <summary>Folder that holds the log files. Created on first write if missing.</summary>
    public required string Directory { get; init; }

    /// <summary>Base file name (without extension). The active file is <c>{FilePrefix}.log</c>.</summary>
    public string FilePrefix { get; init; } = "liveolator";

    /// <summary>Lowest severity written to disk; entries below this are dropped.</summary>
    public LogLevel MinimumLevel { get; init; } = LogLevel.Information;

    /// <summary>The active file is rolled once it reaches this size (bytes). Default 5 MB.</summary>
    public long MaxFileBytes { get; init; } = 5L * 1024 * 1024;

    /// <summary>How many rolled files to keep besides the active one. Older ones are pruned. Default 5.</summary>
    public int MaxRetainedFiles { get; init; } = 5;

    /// <summary>The absolute path of the active log file.</summary>
    public string CurrentFilePath => Path.Combine(Directory, $"{FilePrefix}.log");
}
