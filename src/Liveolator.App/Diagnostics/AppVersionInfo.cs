using System.Diagnostics;
using System.Reflection;

namespace Liveolator.App.Diagnostics;

/// <summary>
/// The running app's release identity — the human-facing version (e.g. <c>0.1.1</c>) and the build
/// identifier (the git commit the build was stamped with, e.g. <c>da8d67e</c>). Surfaced read-only in the
/// Settings → Diagnostics tab so a performer can quote exactly which build they are running for support.
/// </summary>
/// <remarks>
/// The build identifier comes from the assembly's <see cref="AssemblyInformationalVersionAttribute"/>:
/// the SDK appends <c>+&lt;SourceRevisionId&gt;</c> when the csproj stamps the git commit (see the
/// <c>StampGitCommit</c> target). A build made outside a git checkout (or with stamping unavailable)
/// has no commit metadata and reports <see cref="LocalBuild"/>. The parsing is kept pure so it is unit-tested
/// without an assembly.
/// </remarks>
public sealed record AppVersionInfo(string Version, string Build)
{
    /// <summary>Build identifier reported when the assembly carries no commit metadata (e.g. a local build).</summary>
    public const string LocalBuild = "local";

    /// <summary>Reported version when no version metadata is present at all.</summary>
    public const string UnknownVersion = "unknown";

    /// <summary>
    /// Splits an informational version (e.g. <c>"0.1.1+da8d67e"</c>) into its display version and build
    /// identifier. The portion before <c>+</c> is the version; the portion after is the build (commit). When
    /// no <c>+metadata</c> is present, <paramref name="fileVersion"/> backs the version and the build falls
    /// back to <see cref="LocalBuild"/>.
    /// </summary>
    public static AppVersionInfo Parse(string? informationalVersion, string? fileVersion)
    {
        string? informational = NullIfBlank(informationalVersion);
        int plus = informational?.IndexOf('+') ?? -1;

        string version = plus >= 0
            ? informational![..plus]
            : informational ?? NullIfBlank(fileVersion) ?? UnknownVersion;

        string build = plus >= 0 && plus + 1 < informational!.Length
            ? informational[(plus + 1)..]
            : LocalBuild;

        return new AppVersionInfo(version, build);
    }

    /// <summary>Reads the version identity from the entry assembly (the running executable).</summary>
    public static AppVersionInfo FromEntryAssembly()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // Location is empty under single-file publish — fall back to the assembly version in that case.
        string? fileVersion = string.IsNullOrEmpty(assembly.Location)
            ? assembly.GetName().Version?.ToString()
            : FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion;
        return Parse(informational, fileVersion);
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
