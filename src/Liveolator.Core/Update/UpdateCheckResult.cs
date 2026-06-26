namespace Liveolator.Core.Update;

/// <summary>
/// The outcome of an update check: either nothing to offer, or a newer build the user should be told
/// about (carrying the manifest the dialog needs). Produced by <see cref="UpdateAvailabilityChecker"/>.
/// </summary>
/// <param name="IsUpdateAvailable">True when a strictly-newer, non-skipped build was found.</param>
/// <param name="Manifest">The newer build's manifest when <see cref="IsUpdateAvailable"/>; otherwise null.</param>
public sealed record UpdateCheckResult(bool IsUpdateAvailable, UpdateManifest? Manifest)
{
    /// <summary>The result when there is nothing to offer (up to date, unknown, or skipped).</summary>
    public static UpdateCheckResult None { get; } = new(false, null);

    /// <summary>Builds an "update available" result for the given manifest.</summary>
    public static UpdateCheckResult Available(UpdateManifest manifest) => new(true, manifest);
}
