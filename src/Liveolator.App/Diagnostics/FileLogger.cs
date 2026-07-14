using System.Text;
using Microsoft.Extensions.Logging;

namespace Liveolator.App.Diagnostics;

/// <summary>
/// An <see cref="ILogger"/> that formats one entry per event and appends it to the shared
/// <see cref="RollingLogFile"/>. One instance per category; all instances share the file writer so the
/// log is a single chronological stream. The format is greppable and timestamped:
/// <c>2026-06-09 14:03:11.482 +03:00 [Error] Category: message</c>, with the exception (if any) on the
/// following lines.
/// </summary>
internal sealed class FileLogger : ILogger
{
    private readonly string _category;
    private readonly RollingLogFile _file;
    private readonly Func<LogLevel> _minimumLevel;

    public FileLogger(string category, RollingLogFile file, Func<LogLevel> minimumLevel)
    {
        _category = category;
        _file = file;
        _minimumLevel = minimumLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel)
        => logLevel != LogLevel.None && logLevel >= _minimumLevel();

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;
        ArgumentNullException.ThrowIfNull(formatter);

        var builder = new StringBuilder();
        builder.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
        builder.Append(" [").Append(LevelLabel(logLevel)).Append("] ");
        builder.Append(_category).Append(": ");
        builder.Append(formatter(state, exception));
        if (exception is not null)
            builder.Append(Environment.NewLine).Append(exception);

        _file.Append(builder.ToString());
    }

    // Fixed-width, framework-agnostic labels so the file reads consistently regardless of culture.
    private static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Trace => "Trace",
        LogLevel.Debug => "Debug",
        LogLevel.Information => "Information",
        LogLevel.Warning => "Warning",
        LogLevel.Error => "Error",
        LogLevel.Critical => "Critical",
        _ => level.ToString(),
    };
}
