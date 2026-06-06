using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Liveolator.Core.Extensions;

namespace Liveolator.Media.Extensions;

public sealed class ExtensionPackageValidator : IExtensionValidator
{
    public const long DefaultMaximumPackageBytes = 512L * 1024 * 1024;
    public const long DefaultMaximumFileBytes = 256L * 1024 * 1024;
    public const int DefaultMaximumEntries = 2_048;
    public const int DefaultMaximumShaderBytes = 512 * 1024;
    public const string CurrentApiVersion = "1.0.0";

    private static readonly Regex IdentifierPattern =
        new("^[a-z0-9]+(?:[.-][a-z0-9]+)+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ITrustedPublisherStore _publishers;
    private readonly long _maximumPackageBytes;
    private readonly long _maximumFileBytes;
    private readonly int _maximumEntries;

    public ExtensionPackageValidator(
        ITrustedPublisherStore publishers,
        long maximumPackageBytes = DefaultMaximumPackageBytes,
        long maximumFileBytes = DefaultMaximumFileBytes,
        int maximumEntries = DefaultMaximumEntries)
    {
        _publishers = publishers ?? throw new ArgumentNullException(nameof(publishers));
        _maximumPackageBytes = maximumPackageBytes;
        _maximumFileBytes = maximumFileBytes;
        _maximumEntries = maximumEntries;
    }

    public async Task<ExtensionInstallPreview> ValidateAsync(
        string packagePath,
        bool allowUnsigned,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ExtensionValidationIssue>();
        var entries = new List<string>();
        ExtensionManifest? manifest = null;
        string? publisherKeyId = null;
        long totalSize = 0;

        if (!File.Exists(packagePath))
            return Invalid("package.missing", $"Package '{packagePath}' does not exist.");
        if (!string.Equals(Path.GetExtension(packagePath), ".liveolator-pack", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("package.extension", "Package must use the .liveolator-pack extension."));

        try
        {
            await using var stream = new FileStream(
                packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            if (archive.Entries.Count > _maximumEntries)
                issues.Add(new("package.entryCount", $"Package contains more than {_maximumEntries} entries."));

            var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsSymbolicLink(entry))
                {
                    issues.Add(new("package.symlink", $"Symbolic-link entry '{entry.FullName}' is not allowed."));
                    continue;
                }
                if (IsDirectory(entry))
                    continue;
                string? path = NormalizeEntryPath(entry.FullName);
                if (path is null)
                {
                    issues.Add(new("package.path", $"Unsafe package path '{entry.FullName}'."));
                    continue;
                }
                if (!normalized.Add(path))
                    issues.Add(new("package.duplicate", $"Duplicate package path '{path}'."));
                if (entry.Length > _maximumFileBytes)
                    issues.Add(new("package.fileSize", $"File '{path}' exceeds the per-file size limit."));
                if (path.EndsWith(".glsl", StringComparison.OrdinalIgnoreCase)
                    && entry.Length > DefaultMaximumShaderBytes)
                    issues.Add(new("visual.shaderSize", $"Shader '{path}' exceeds the shader size limit."));

                totalSize = checked(totalSize + entry.Length);
                entries.Add(path);
            }
            if (totalSize > _maximumPackageBytes)
                issues.Add(new("package.totalSize", "Package exceeds the uncompressed size limit."));

            ZipArchiveEntry? manifestEntry = Find(archive, "manifest.json");
            if (manifestEntry is null)
                issues.Add(new("manifest.missing", "Package has no manifest.json."));
            else
            {
                byte[] manifestBytes = await ReadAllAsync(manifestEntry, cancellationToken).ConfigureAwait(false);
                try
                {
                    manifest = JsonSerializer.Deserialize<ExtensionManifest>(manifestBytes, JsonOptions);
                }
                catch (JsonException ex)
                {
                    issues.Add(new("manifest.json", $"Manifest is invalid JSON: {ex.Message}"));
                }

                if (manifest is not null)
                {
                    ValidateManifest(manifest, issues);
                    await ValidateFilesAsync(archive, manifest, issues, cancellationToken).ConfigureAwait(false);

                    ZipArchiveEntry? signatureEntry = Find(archive, "signature.json");
                    if (signatureEntry is null)
                    {
                        if (!allowUnsigned)
                            issues.Add(new("signature.missing", "Package is unsigned."));
                    }
                    else
                    {
                        ExtensionSignature? signature = await DeserializeAsync<ExtensionSignature>(
                            signatureEntry, cancellationToken).ConfigureAwait(false);
                        if (signature is null || string.IsNullOrWhiteSpace(signature.PublisherKeyId))
                            issues.Add(new("signature.invalid", "signature.json is invalid."));
                        else
                        {
                            publisherKeyId = signature.PublisherKeyId;
                            VerifySignature(manifestBytes, signature, issues);
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or OverflowException)
        {
            issues.Add(new("package.read", $"Package cannot be read: {ex.Message}"));
        }

        var validation = new ExtensionValidationResult(
            issues.Count == 0, manifest, publisherKeyId, issues);
        return new ExtensionInstallPreview(validation, totalSize, entries);
    }

    private void VerifySignature(
        byte[] manifestBytes,
        ExtensionSignature signature,
        List<ExtensionValidationIssue> issues)
    {
        if (!_publishers.TryGetPublicKey(signature.PublisherKeyId, out string publicKeyPem))
        {
            issues.Add(new("signature.publisher", $"Publisher key '{signature.PublisherKeyId}' is not trusted."));
            return;
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(signature.Signature);
            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);
            if (!ecdsa.VerifyData(manifestBytes, bytes, HashAlgorithmName.SHA256))
                issues.Add(new("signature.verify", "Package signature does not match manifest.json."));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            issues.Add(new("signature.invalid", $"Package signature is invalid: {ex.Message}"));
        }
    }

    private static void ValidateManifest(
        ExtensionManifest manifest,
        List<ExtensionValidationIssue> issues)
    {
        if (!IdentifierPattern.IsMatch(manifest.PackageId ?? string.Empty))
            issues.Add(new("manifest.packageId", "packageId must be a reverse-domain style identifier."));
        if (!IsSemanticVersion(manifest.Version))
            issues.Add(new("manifest.version", "Package version must be semantic versioning."));
        if (!IsSemanticVersion(manifest.RequiredApiVersion))
            issues.Add(new("manifest.apiVersion", "Required API version must be semantic versioning."));
        else if (Major(manifest.RequiredApiVersion) != Major(CurrentApiVersion))
            issues.Add(new("manifest.apiCompatibility", $"Package requires incompatible API {manifest.RequiredApiVersion}."));
        if (string.IsNullOrWhiteSpace(manifest.Publisher))
            issues.Add(new("manifest.publisher", "Publisher is required."));
        if (manifest.Content == ExtensionContentKind.None)
            issues.Add(new("manifest.content", "Package must declare at least one content type."));
        foreach (ExtensionDependency dependency in manifest.Dependencies ?? Array.Empty<ExtensionDependency>())
        {
            if (!IdentifierPattern.IsMatch(dependency.PackageId ?? string.Empty)
                || !IsSemanticVersion(dependency.MinimumVersion))
                issues.Add(new("manifest.dependency", "Extension dependency is invalid."));
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<ExtensionFile> files = manifest.Files ?? Array.Empty<ExtensionFile>();
        if (manifest.Files is null)
            issues.Add(new("manifest.files", "Manifest files collection is required."));
        foreach (ExtensionFile file in files)
        {
            string? path = NormalizeEntryPath(file.Path);
            if (path is null || IsMetadataPath(path))
                issues.Add(new("manifest.filePath", $"Manifest contains unsafe file path '{file.Path}'."));
            else if (!paths.Add(path))
                issues.Add(new("manifest.fileDuplicate", $"Manifest repeats file '{path}'."));
            if (file.Size < 0)
                issues.Add(new("manifest.fileSize", $"Manifest file '{file.Path}' has a negative size."));
            if (file.Sha256?.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                issues.Add(new("manifest.hash", $"Manifest file '{file.Path}' has an invalid SHA-256."));
        }
    }

    private static async Task ValidateFilesAsync(
        ZipArchive archive,
        ExtensionManifest manifest,
        List<ExtensionValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ExtensionFile> files = manifest.Files ?? Array.Empty<ExtensionFile>();
        var declared = new HashSet<string>(
            files.Select(f => NormalizeEntryPath(f.Path)!).Where(p => p is not null),
            StringComparer.OrdinalIgnoreCase);

        foreach (ExtensionFile file in files)
        {
            string? path = NormalizeEntryPath(file.Path);
            if (path is null)
                continue;
            ZipArchiveEntry? entry = Find(archive, path);
            if (entry is null)
            {
                issues.Add(new("file.missing", $"Declared file '{path}' is missing."));
                continue;
            }
            if (entry.Length != file.Size)
                issues.Add(new("file.size", $"File '{path}' size does not match the manifest."));

            await using Stream content = entry.Open();
            byte[] hash = await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(Convert.ToHexString(hash), file.Sha256, StringComparison.OrdinalIgnoreCase))
                issues.Add(new("file.hash", $"File '{path}' hash does not match the manifest."));
        }

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (IsDirectory(entry))
                continue;
            string? path = NormalizeEntryPath(entry.FullName);
            if (path is not null && !IsMetadataPath(path) && !declared.Contains(path))
                issues.Add(new("file.undeclared", $"Package file '{path}' is not declared in the manifest."));
        }
    }

    internal static string? NormalizeEntryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            return null;
        string normalized = path.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0 || normalized.Split('/').Any(p => p is "" or "." or ".."))
            return null;
        return normalized;
    }

    private static bool IsMetadataPath(string path)
        => string.Equals(path, "manifest.json", StringComparison.OrdinalIgnoreCase)
           || string.Equals(path, "signature.json", StringComparison.OrdinalIgnoreCase);

    private static bool IsDirectory(ZipArchiveEntry entry)
        => entry.FullName.EndsWith("/", StringComparison.Ordinal)
           || entry.FullName.EndsWith("\\", StringComparison.Ordinal);

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
        => ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    private static ZipArchiveEntry? Find(ZipArchive archive, string path)
        => archive.Entries.FirstOrDefault(
            e => string.Equals(NormalizeEntryPath(e.FullName), path, StringComparison.OrdinalIgnoreCase));

    private static async Task<byte[]> ReadAllAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using Stream stream = entry.Open();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }

    private static async Task<T?> DeserializeAsync<T>(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using Stream stream = entry.Open();
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static bool IsSemanticVersion(string? value)
        => value is not null && Regex.IsMatch(
            value, @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$");

    private static int Major(string version) => int.Parse(version.Split('.')[0]);

    private static ExtensionInstallPreview Invalid(string code, string message)
    {
        var issue = new ExtensionValidationIssue(code, message);
        return new ExtensionInstallPreview(
            new ExtensionValidationResult(false, null, null, new[] { issue }),
            0,
            Array.Empty<string>());
    }
}
