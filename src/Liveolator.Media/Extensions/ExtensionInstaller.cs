using System.IO.Compression;
using Liveolator.Core.Extensions;

namespace Liveolator.Media.Extensions;

public sealed class ExtensionInstaller : IExtensionInstaller
{
    private readonly ExtensionPackageValidator _validator;
    private readonly ExtensionCatalog _catalog;
    private readonly bool _developerMode;

    public ExtensionInstaller(
        ExtensionPackageValidator validator,
        ExtensionCatalog catalog,
        bool developerMode = false)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _developerMode = developerMode;
    }

    public Task<ExtensionInstallPreview> PreviewAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
        => _validator.ValidateAsync(packagePath, _developerMode, cancellationToken);

    public async Task<InstalledExtension> InstallAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_catalog.RootDirectory);
        string privatePackage = Path.Combine(
            _catalog.RootDirectory, $".incoming-{Guid.NewGuid():N}.liveolator-pack");
        File.Copy(packagePath, privatePackage, overwrite: false);

        ExtensionInstallPreview preview = await _validator.ValidateAsync(
            privatePackage, _developerMode, cancellationToken).ConfigureAwait(false);
        if (!preview.Validation.IsValid || preview.Validation.Manifest is null)
        {
            File.Delete(privatePackage);
            throw new InvalidDataException(string.Join(
                Environment.NewLine, preview.Validation.Issues.Select(i => $"{i.Code}: {i.Message}")));
        }

        ExtensionManifest manifest = preview.Validation.Manifest;
        try
        {
            EnsureDependencies(manifest);
        }
        catch
        {
            File.Delete(privatePackage);
            throw;
        }
        string destination = _catalog.InstallPath(manifest.PackageId, manifest.Version);
        if (Directory.Exists(destination))
        {
            File.Delete(privatePackage);
            throw new InvalidOperationException(
                $"Extension '{manifest.PackageId}' version '{manifest.Version}' is already installed.");
        }

        string parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        string staging = destination + $".install-{Guid.NewGuid():N}";

        try
        {
            Directory.CreateDirectory(staging);
            using var archive = ZipFile.OpenRead(privatePackage);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal)
                    || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                    continue;
                string? relative = ExtensionPackageValidator.NormalizeEntryPath(entry.FullName);
                if (relative is null)
                    throw new InvalidDataException($"Unsafe package path '{entry.FullName}'.");

                string output = Path.GetFullPath(Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar)));
                string stagingRoot = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
                if (!output.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Package path '{relative}' escapes the install directory.");

                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                await using Stream input = entry.Open();
                await using var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            Directory.Move(staging, destination);

            DateTimeOffset installedAt = DateTimeOffset.UtcNow;
            ExtensionRegistrySnapshot registry = await _catalog.LoadRegistryAsync(cancellationToken).ConfigureAwait(false);
            var entries = registry.Extensions
                .Where(e => e.PackageId != manifest.PackageId || e.Version != manifest.Version)
                .Append(new ExtensionRegistryEntry(
                    manifest.PackageId,
                    manifest.Version,
                    IsEnabled: true,
                    installedAt,
                    preview.Validation.PublisherKeyId ?? "developer-unsigned"))
                .ToArray();
            await _catalog.SaveRegistryAsync(
                new ExtensionRegistrySnapshot(ExtensionRegistrySnapshot.CurrentVersion, entries),
                cancellationToken).ConfigureAwait(false);
            await _catalog.RefreshAsync(cancellationToken).ConfigureAwait(false);

            return _catalog.Installed.Single(
                e => e.Manifest.PackageId == manifest.PackageId && e.Manifest.Version == manifest.Version);
        }
        catch
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            if (Directory.Exists(destination)
                && !_catalog.Installed.Any(e => string.Equals(e.InstallPath, destination, StringComparison.OrdinalIgnoreCase)))
                Directory.Delete(destination, recursive: true);
            throw;
        }
        finally
        {
            try { File.Delete(privatePackage); } catch (IOException) { }
        }
    }

    public async Task SetEnabledAsync(
        string packageId,
        string version,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ExtensionRegistrySnapshot registry = await _catalog.LoadRegistryAsync(cancellationToken).ConfigureAwait(false);
        bool found = false;
        ExtensionRegistryEntry[] entries = registry.Extensions.Select(e =>
        {
            if (e.PackageId == packageId && e.Version == version)
            {
                found = true;
                return e with { IsEnabled = enabled };
            }
            return e;
        }).ToArray();
        if (!found)
            throw new KeyNotFoundException($"Extension '{packageId}' version '{version}' is not installed.");

        await _catalog.SaveRegistryAsync(
            new ExtensionRegistrySnapshot(ExtensionRegistrySnapshot.CurrentVersion, entries),
            cancellationToken).ConfigureAwait(false);
        await _catalog.RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UninstallAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        ExtensionRegistrySnapshot registry = await _catalog.LoadRegistryAsync(cancellationToken).ConfigureAwait(false);
        string destination = _catalog.InstallPath(packageId, version);
        ExtensionRegistryEntry[] entries = registry.Extensions
            .Where(e => e.PackageId != packageId || e.Version != version)
            .ToArray();

        if (Directory.Exists(destination))
            Directory.Delete(destination, recursive: true);
        string? packageDirectory = Path.GetDirectoryName(destination);
        if (packageDirectory is not null
            && Directory.Exists(packageDirectory)
            && !Directory.EnumerateFileSystemEntries(packageDirectory).Any())
            Directory.Delete(packageDirectory);

        await _catalog.SaveRegistryAsync(
            new ExtensionRegistrySnapshot(ExtensionRegistrySnapshot.CurrentVersion, entries),
            cancellationToken).ConfigureAwait(false);
        await _catalog.RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    private void EnsureDependencies(ExtensionManifest manifest)
    {
        foreach (ExtensionDependency dependency in manifest.Dependencies ?? Array.Empty<ExtensionDependency>())
        {
            bool satisfied = _catalog.Installed.Any(installed =>
                installed.IsEnabled
                && installed.Manifest.PackageId == dependency.PackageId
                && VersionAtLeast(installed.Manifest.Version, dependency.MinimumVersion));
            if (!satisfied)
                throw new InvalidOperationException(
                    $"Extension dependency '{dependency.PackageId}' >= {dependency.MinimumVersion} is not installed.");
        }
    }

    private static bool VersionAtLeast(string actual, string minimum)
    {
        static Version Parse(string value) => Version.Parse(value.Split('-', '+')[0]);
        return Parse(actual) >= Parse(minimum);
    }
}
