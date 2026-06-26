namespace Liveolator.Core.Update;

/// <summary>
/// Retrieves the latest-build <see cref="UpdateManifest"/> from wherever releases are published (the
/// marketing website's <c>version.json</c>). A seam so the network transport lives in a binding
/// (<c>Liveolator.Online</c>) and the startup check unit-tests with a fake. Offline-first: any failure
/// (no network, non-success status, malformed body) resolves to <c>null</c> rather than throwing, so a
/// failed check never blocks or crashes app startup (global standards #16/#26).
/// </summary>
public interface IUpdateManifestSource
{
    /// <summary>Fetches the latest published manifest, or <c>null</c> when it cannot be retrieved/parsed.</summary>
    Task<UpdateManifest?> FetchAsync(CancellationToken cancellationToken = default);
}
