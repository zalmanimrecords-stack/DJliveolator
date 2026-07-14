namespace Liveolator.Core.Settings;

/// <summary>
/// Persisted preferences for the startup update check (doc 12 Settings tab). Controls whether the app
/// checks the website for a newer build on launch, and remembers a version the user chose to skip so the
/// same one is not offered again. Pure data — persisted via <c>ISettingsStore</c>; the actual check lives
/// behind seams in the app host.
/// </summary>
/// <param name="CheckOnStartup">When true (default), the app checks for a newer build at launch.</param>
/// <param name="SkippedVersion">A version the user dismissed with "Skip this version", or null.</param>
public sealed record UpdateSettings(bool CheckOnStartup = true, string? SkippedVersion = null)
{
    /// <summary>The default preferences: checking enabled, nothing skipped.</summary>
    public static UpdateSettings Default { get; } = new();

    /// <summary>Returns a copy with a blank skipped-version folded to null (so it is treated as "none").</summary>
    public UpdateSettings Normalized()
        => this with { SkippedVersion = string.IsNullOrWhiteSpace(SkippedVersion) ? null : SkippedVersion.Trim() };
}
