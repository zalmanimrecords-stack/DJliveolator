namespace Liveolator.Media.Extensions;

internal sealed record ExtensionSignature(string PublisherKeyId, string Signature);

internal sealed record ExtensionRegistrySnapshot(
    int Version,
    IReadOnlyList<ExtensionRegistryEntry> Extensions)
{
    public const int CurrentVersion = 1;
}

internal sealed record ExtensionRegistryEntry(
    string PackageId,
    string Version,
    bool IsEnabled,
    DateTimeOffset InstalledAt,
    string PublisherKeyId);
