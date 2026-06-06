namespace Liveolator.Core.Extensions;

[Flags]
public enum ExtensionContentKind
{
    None = 0,
    VisualEffects = 1,
    VisualShow = 2,
    UiTheme = 4,
}

public sealed record ExtensionDependency(string PackageId, string MinimumVersion);

public sealed record ExtensionFile(string Path, string Sha256, long Size);

public sealed record ExtensionManifest(
    string PackageId,
    string Version,
    string RequiredApiVersion,
    string Publisher,
    ExtensionContentKind Content,
    IReadOnlyList<ExtensionDependency> Dependencies,
    IReadOnlyList<ExtensionFile> Files);

public sealed record ExtensionValidationIssue(string Code, string Message);

public sealed record ExtensionValidationResult(
    bool IsValid,
    ExtensionManifest? Manifest,
    string? PublisherKeyId,
    IReadOnlyList<ExtensionValidationIssue> Issues);

public sealed record InstalledExtension(
    ExtensionManifest Manifest,
    string InstallPath,
    bool IsEnabled,
    DateTimeOffset InstalledAt,
    ExtensionValidationResult Validation);

public sealed record ExtensionInstallPreview(
    ExtensionValidationResult Validation,
    long UncompressedSize,
    IReadOnlyList<string> Entries);

public interface IExtensionValidator
{
    Task<ExtensionInstallPreview> ValidateAsync(
        string packagePath,
        bool allowUnsigned,
        CancellationToken cancellationToken = default);
}

public interface IExtensionInstaller
{
    Task<ExtensionInstallPreview> PreviewAsync(string packagePath, CancellationToken cancellationToken = default);
    Task<InstalledExtension> InstallAsync(string packagePath, CancellationToken cancellationToken = default);
    Task SetEnabledAsync(string packageId, string version, bool enabled, CancellationToken cancellationToken = default);
    Task UninstallAsync(string packageId, string version, CancellationToken cancellationToken = default);
}

public interface IExtensionCatalog
{
    IReadOnlyList<InstalledExtension> Installed { get; }
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

public interface IExtensionContentReloader
{
    Task ReloadAsync(CancellationToken cancellationToken = default);
}

public interface ITrustedPublisherStore
{
    bool TryGetPublicKey(string publisherKeyId, out string subjectPublicKeyInfoPem);
}
