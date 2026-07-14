using Microsoft.Extensions.Logging;

namespace Liveolator.App.Diagnostics;

/// <summary>
/// Composition-root helpers for the on-disk log: where it lives, how to read the persisted verbosity,
/// how to build the <see cref="ILoggerFactory"/> over the <see cref="FileLoggerProvider"/>, and how to
/// route otherwise-unhandled exceptions into it. This is the one place the app turns "log to a file"
/// into wiring; engines/UI just take <c>ILogger</c> from DI.
/// </summary>
public static class AppLogging
{
    /// <summary>Parses a persisted <c>DiagnosticsSettings.MinimumLevel</c> string into a <see cref="LogLevel"/>.</summary>
    public static LogLevel ParseLevel(string? minimumLevel)
        => Enum.TryParse(minimumLevel, ignoreCase: true, out LogLevel level) ? level : LogLevel.Information;

    /// <summary>
    /// Builds an <see cref="ILoggerFactory"/> writing to the file sink described by
    /// <paramref name="options"/>. Disposing the factory disposes the provider (and flushes/closes the file).
    /// </summary>
    public static ILoggerFactory CreateFactory(FileLoggerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(options.MinimumLevel);
            builder.AddProvider(new FileLoggerProvider(options));
        });
    }

    /// <summary>
    /// Routes process-wide unhandled exceptions (AppDomain + unobserved Task) into the log so a crash or
    /// a swallowed async failure leaves a trace for debugging. Safe to call once at startup.
    /// </summary>
    public static void InstallGlobalExceptionLogging(ILoggerFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ILogger logger = factory.CreateLogger("Liveolator.Crash");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            logger.LogCritical(
                e.ExceptionObject as Exception,
                "Unhandled exception (terminating={Terminating}).",
                e.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogError(e.Exception, "Unobserved task exception.");
            e.SetObserved(); // already logged; don't escalate to a crash
        };
    }
}
