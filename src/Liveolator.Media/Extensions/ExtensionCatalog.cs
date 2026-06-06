using System.Text.Json;
using System.Text.Json.Serialization;
using Liveolator.Core.Extensions;

namespace Liveolator.Media.Extensions;

public sealed class ExtensionCatalog : IExtensionCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _root;
    private readonly string _registryPath;
    private readonly Action<string>? _onWarning;
    private InstalledExtension[] _installed = Array.Empty<InstalledExtension>();

    public ExtensionCatalog(string? rootDirectory = null, Action<string>? onWarning = null)
    {
        _root = Path.Combine(rootDirectory ?? JsonCatalogStore.DefaultRoot(), "extensions");
        _registryPath = Path.Combine(_root, "registry.json");
        _onWarning = onWarning;
    }

    public string RootDirectory => _root;
    public string RegistryPath => _registryPath;
    public IReadOnlyList<InstalledExtension> Installed => Volatile.Read(ref _installed);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ExtensionRegistrySnapshot registry = await LoadRegistryAsync(cancellationToken).ConfigureAwait(false);
        var installed = new List<InstalledExtension>();
        foreach (ExtensionRegistryEntry entry in registry.Extensions)
        {
            string installPath = InstallPath(entry.PackageId, entry.Version);
            string manifestPath = Path.Combine(installPath, "manifest.json");
            try
            {
                if (!File.Exists(manifestPath))
                    continue;
                await using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                ExtensionManifest? manifest = await JsonSerializer.DeserializeAsync<ExtensionManifest>(
                    stream, JsonOptions, cancellationToken).ConfigureAwait(false);
                if (manifest is null)
                    continue;

                var validation = new ExtensionValidationResult(
                    true, manifest, entry.PublisherKeyId, Array.Empty<ExtensionValidationIssue>());
                installed.Add(new InstalledExtension(
                    manifest, installPath, entry.IsEnabled, entry.InstalledAt, validation));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _onWarning?.Invoke($"Installed extension '{entry.PackageId}' is unreadable ({ex.Message}).");
            }
        }
        Volatile.Write(ref _installed, installed.ToArray());
    }

    internal string InstallPath(string packageId, string version) => Path.Combine(_root, packageId, version);

    internal async Task<ExtensionRegistrySnapshot> LoadRegistryAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_registryPath))
            return new ExtensionRegistrySnapshot(
                ExtensionRegistrySnapshot.CurrentVersion, Array.Empty<ExtensionRegistryEntry>());
        try
        {
            await using var stream = new FileStream(_registryPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            ExtensionRegistrySnapshot? registry = await JsonSerializer.DeserializeAsync<ExtensionRegistrySnapshot>(
                stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return registry?.Version == ExtensionRegistrySnapshot.CurrentVersion
                ? registry
                : new ExtensionRegistrySnapshot(
                    ExtensionRegistrySnapshot.CurrentVersion, Array.Empty<ExtensionRegistryEntry>());
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke($"Extension registry is unreadable ({ex.Message}); using an empty registry.");
            return new ExtensionRegistrySnapshot(
                ExtensionRegistrySnapshot.CurrentVersion, Array.Empty<ExtensionRegistryEntry>());
        }
    }

    internal async Task SaveRegistryAsync(
        ExtensionRegistrySnapshot registry,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        string temp = _registryPath + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(stream, registry, JsonOptions, cancellationToken).ConfigureAwait(false);
        File.Move(temp, _registryPath, overwrite: true);
    }
}
