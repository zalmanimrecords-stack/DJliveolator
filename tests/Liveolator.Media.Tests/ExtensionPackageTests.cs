using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Liveolator.Core.Extensions;
using Liveolator.Media.Extensions;

namespace Liveolator.Media.Tests;

public sealed class ExtensionPackageTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "liveolator-extension-tests", Guid.NewGuid().ToString("N"));

    public ExtensionPackageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task SignedPackage_ValidatesInstallsAndUninstallsAtomically()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string package = CreatePackage(
            key,
            new Dictionary<string, byte[]> { ["themes/night.json"] = "{}"u8.ToArray() });
        var publishers = new DictionaryTrustedPublisherStore(
            new Dictionary<string, string> { ["test-key"] = key.ExportSubjectPublicKeyInfoPem() });
        var validator = new ExtensionPackageValidator(publishers);
        var catalog = new ExtensionCatalog(_root);
        var installer = new ExtensionInstaller(validator, catalog);

        ExtensionInstallPreview preview = await installer.PreviewAsync(package);
        InstalledExtension installed = await installer.InstallAsync(package);

        Assert.True(preview.Validation.IsValid);
        Assert.True(File.Exists(Path.Combine(installed.InstallPath, "themes", "night.json")));
        Assert.False(Directory.EnumerateDirectories(
            Path.GetDirectoryName(installed.InstallPath)!, "*.install-*").Any());

        await installer.SetEnabledAsync("com.example.pack", "1.2.3", false);
        Assert.False(Assert.Single(catalog.Installed).IsEnabled);
        await installer.UninstallAsync("com.example.pack", "1.2.3");
        Assert.Empty(catalog.Installed);
    }

    [Fact]
    public async Task TamperedFile_IsRejected()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string package = CreatePackage(
            key,
            new Dictionary<string, byte[]> { ["shaders/effect.glsl"] = "original"u8.ToArray() });
        using (ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            ZipArchiveEntry entry = archive.GetEntry("shaders/effect.glsl")!;
            entry.Delete();
            ZipArchiveEntry replacement = archive.CreateEntry("shaders/effect.glsl");
            await using Stream stream = replacement.Open();
            await stream.WriteAsync("tampered"u8.ToArray());
        }
        var validator = new ExtensionPackageValidator(new DictionaryTrustedPublisherStore(
            new Dictionary<string, string> { ["test-key"] = key.ExportSubjectPublicKeyInfoPem() }));

        ExtensionInstallPreview preview = await validator.ValidateAsync(package, allowUnsigned: false);

        Assert.False(preview.Validation.IsValid);
        Assert.Contains(preview.Validation.Issues, i => i.Code is "file.hash" or "file.size");
    }

    [Fact]
    public async Task ZipTraversalAndUnsignedPackage_AreRejected()
    {
        string path = Path.Combine(_root, "unsafe.liveolator-pack");
        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            archive.CreateEntry("../escape.txt");
            WriteEntry(archive, "manifest.json", "{}"u8.ToArray());
        }
        var validator = new ExtensionPackageValidator(new DictionaryTrustedPublisherStore());

        ExtensionInstallPreview preview = await validator.ValidateAsync(path, allowUnsigned: false);

        Assert.False(preview.Validation.IsValid);
        Assert.Contains(preview.Validation.Issues, i => i.Code == "package.path");
    }

    [Fact]
    public async Task UnsignedPackage_IsAllowedOnlyInDeveloperMode()
    {
        string package = CreatePackage(
            key: null,
            new Dictionary<string, byte[]> { ["themes/night.json"] = "{}"u8.ToArray() });
        var validator = new ExtensionPackageValidator(new DictionaryTrustedPublisherStore());

        Assert.False((await validator.ValidateAsync(package, allowUnsigned: false)).Validation.IsValid);
        Assert.True((await validator.ValidateAsync(package, allowUnsigned: true)).Validation.IsValid);
    }

    private string CreatePackage(ECDsa? key, IReadOnlyDictionary<string, byte[]> files)
    {
        string path = Path.Combine(_root, $"{Guid.NewGuid():N}.liveolator-pack");
        ExtensionFile[] declared = files.Select(pair => new ExtensionFile(
            pair.Key,
            Convert.ToHexString(SHA256.HashData(pair.Value)),
            pair.Value.Length)).ToArray();
        var manifest = new ExtensionManifest(
            "com.example.pack",
            "1.2.3",
            "1.0.0",
            "Example",
            ExtensionContentKind.VisualEffects | ExtensionContentKind.UiTheme,
            Array.Empty<ExtensionDependency>(),
            declared);
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);

        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "manifest.json", manifestBytes);
        foreach ((string filePath, byte[] contents) in files)
            WriteEntry(archive, filePath, contents);
        if (key is not null)
        {
            byte[] signature = key.SignData(manifestBytes, HashAlgorithmName.SHA256);
            byte[] signatureJson = JsonSerializer.SerializeToUtf8Bytes(
                new { PublisherKeyId = "test-key", Signature = Convert.ToBase64String(signature) },
                JsonOptions);
            WriteEntry(archive, "signature.json", signatureJson);
        }
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] contents)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        using Stream stream = entry.Open();
        stream.Write(contents);
    }
}
