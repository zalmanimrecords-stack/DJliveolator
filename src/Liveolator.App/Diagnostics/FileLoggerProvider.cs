using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Liveolator.App.Diagnostics;

/// <summary>
/// The <see cref="ILoggerProvider"/> that backs every category with a <see cref="FileLogger"/> writing
/// to one shared <see cref="RollingLogFile"/>. Registered on an <see cref="ILoggerFactory"/> in the
/// composition root so all engine/UI loggers resolved from DI land in the same on-disk file. Owns the
/// file and disposes it when the factory is disposed.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly RollingLogFile _file;
    private readonly LogLevel _minimumLevel;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);

    public FileLoggerProvider(FileLoggerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _file = new RollingLogFile(options);
        _minimumLevel = options.MinimumLevel;
    }

    /// <summary>The active log file path, so the Settings tab can point the performer at it.</summary>
    public string CurrentFilePath => _file.CurrentFilePath;

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _file, () => _minimumLevel));

    public void Dispose() => _file.Dispose();
}
