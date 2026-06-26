namespace Liveolator.Core.Update;

/// <summary>
/// The machine-readable description of the latest published build, fetched from the website
/// (<c>version.json</c>). Pure data — the transport that retrieves it is a binding behind
/// <see cref="IUpdateManifestSource"/>; the decision of whether it represents a newer build is the pure
/// <see cref="UpdateAvailabilityChecker"/>.
/// </summary>
/// <param name="Version">The latest published version string (e.g. <c>0.1.5</c>).</param>
/// <param name="DownloadUrl">Absolute URL the user opens to download the installer for that version.</param>
/// <param name="Notes">Release-note bullet lines for that version (may be empty).</param>
public sealed record UpdateManifest(string Version, string DownloadUrl, IReadOnlyList<string> Notes)
{
    /// <summary>An empty notes list, reused so callers never have to allocate one.</summary>
    public static IReadOnlyList<string> NoNotes { get; } = Array.Empty<string>();
}
