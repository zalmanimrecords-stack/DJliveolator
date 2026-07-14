namespace Liveolator.Core.Settings;

/// <summary>
/// Persisted diagnostics/logging preferences (doc 12 Settings tab). Controls how verbose the on-disk
/// log file is. Pure data — persisted via <c>ISettingsStore</c>; the concrete file sink and its path
/// live in the app host (no native/IO in Core). The level is stored as a string so Core does not take
/// a dependency on the logging framework's <c>LogLevel</c>; <see cref="Normalized"/> folds an unknown
/// value back to the default.
/// </summary>
/// <param name="MinimumLevel">
/// Lowest severity written to the log file. One of <see cref="Levels"/> (case-insensitive):
/// Trace, Debug, Information, Warning, Error, Critical, None.
/// </param>
// The parameter default is a literal (a primary-ctor parameter default cannot reference a body-declared
// const); it MUST equal DefaultMinimumLevel below.
public sealed record DiagnosticsSettings(string MinimumLevel = "Warning")
{
    /// <summary>The default minimum level: keep Warning and above (Warning/Error/Critical) so the log
    /// captures problems without the noise of routine Information/Debug/Trace entries.</summary>
    public const string DefaultMinimumLevel = "Warning";

    /// <summary>The accepted minimum-level names, in increasing severity (matching the logging framework).</summary>
    public static IReadOnlyList<string> Levels { get; } = new[]
    {
        "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None",
    };

    /// <summary>The default diagnostics preferences.</summary>
    public static DiagnosticsSettings Default { get; } = new();

    /// <summary>Returns a copy with the level folded to its canonical casing, or the default if unknown.</summary>
    public DiagnosticsSettings Normalized()
    {
        string? match = Levels.FirstOrDefault(
            level => string.Equals(level, MinimumLevel, StringComparison.OrdinalIgnoreCase));
        return this with { MinimumLevel = match ?? DefaultMinimumLevel };
    }
}
