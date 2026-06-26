namespace Liveolator.Core.Update;

/// <summary>
/// Pure decision: given the running version, the latest published manifest, and the version (if any) the
/// user chose to skip, decide whether to surface an "update available" prompt. Kept free of IO/UI so the
/// rule is unit-tested exhaustively.
/// </summary>
/// <remarks>
/// An update is offered only when the manifest version parses to a strictly-greater
/// <see cref="System.Version"/> than the running one, and it is not the exact version the user skipped.
/// Anything ambiguous (null manifest, unparsable version on either side) resolves to
/// <see cref="UpdateCheckResult.None"/> — the conservative choice that never nags on bad data.
/// </remarks>
public static class UpdateAvailabilityChecker
{
    /// <summary>Evaluates whether <paramref name="manifest"/> represents a newer build worth prompting for.</summary>
    /// <param name="installedVersion">The running build's version (e.g. <c>0.1.4</c>).</param>
    /// <param name="manifest">The latest published manifest, or null when the fetch failed.</param>
    /// <param name="skippedVersion">A version the user previously chose to skip, or null.</param>
    public static UpdateCheckResult Evaluate(
        string? installedVersion, UpdateManifest? manifest, string? skippedVersion)
    {
        if (manifest is null
            || !TryParseVersion(manifest.Version, out Version latest)
            || !TryParseVersion(installedVersion, out Version current))
            return UpdateCheckResult.None;

        if (latest <= current)
            return UpdateCheckResult.None;

        // The user dismissed exactly this version — honour it until a still-newer build appears. The
        // skipped value is the manifest's own version string (persisted verbatim on Skip), so a parsed
        // equality check matches it back reliably.
        if (TryParseVersion(skippedVersion, out Version skipped) && skipped == latest)
            return UpdateCheckResult.None;

        return UpdateCheckResult.Available(manifest);
    }

    // Parses a release version string into a comparable Version. Tolerant of a leading "v" and of a
    // SemVer pre-release/build suffix ("-beta", "+meta") by comparing only the numeric core — anything
    // that still does not parse yields false so the caller stays conservative.
    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string core = value.Trim().TrimStart('v', 'V');
        int cut = core.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0)
            core = core[..cut];

        return Version.TryParse(core, out Version? parsed) && (version = parsed) is not null;
    }
}
