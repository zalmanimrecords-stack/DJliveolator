using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Liveolator.Core.Extensions;
using Liveolator.Core.Settings;
using Liveolator.Core.Visuals;
using Liveolator.Media.Extensions;

namespace Liveolator.Media.Tests;

/// <summary>
/// Proves the visual add-on standard (doc 26) end-to-end for a third-party <b>generator</b> pack: a signed
/// <c>.liveolator-pack</c> carrying a <c>visual-effects.json</c> with a <see cref="VisualEffectRole.Generator"/>
/// descriptor installs, and <see cref="ExtensionContentLoader"/> registers it (probe-validated) so a scene
/// layer could reference it. Uses a fake shader probe so it runs without the native helper or a GPU.
/// </summary>
public sealed class GeneratorAddonRoundTripTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "liveolator-generator-addon-tests", Guid.NewGuid().ToString("N"));

    public GeneratorAddonRoundTripTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    // A probe that accepts the shader and reports the uniforms the descriptor declares, standing in for
    // the native isolated probe (absent in CI). Returns invalid if asked for an unknown shader.
    private sealed class FakeShaderProbe : IVisualShaderProbe
    {
        private readonly string[] _uniforms;
        public FakeShaderProbe(params string[] uniforms) => _uniforms = uniforms;

        public Task<VisualShaderProbeResult> ProbeAsync(string shaderPath, CancellationToken cancellationToken = default)
            => Task.FromResult(new VisualShaderProbeResult(File.Exists(shaderPath), null, _uniforms));
    }

    [Fact]
    public async Task GeneratorPack_InstallsAndRegistersWithGeneratorRole()
    {
        const string packageId = "com.example.meters";
        const string effectId = "com.example.meters/vu";

        var descriptors = new[]
        {
            new VisualEffectDescriptor(
                effectId, "1.0.0", packageId, "shaders/vu.frag",
                new[] { new VisualEffectParameter("redline", "uRedline", 0, 1, 0.85) },
                Role: VisualEffectRole.Generator),
        };
        byte[] effectsJson = JsonSerializer.SerializeToUtf8Bytes(descriptors, JsonOptions);
        byte[] shader = "#version 330 core\nuniform float uRedline;\nout vec4 fragColor;\nvoid main(){fragColor=vec4(uRedline);}"u8.ToArray();

        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string package = CreatePackage(key, packageId, new Dictionary<string, byte[]>
        {
            ["visual-effects.json"] = effectsJson,
            ["shaders/vu.frag"] = shader,
        });

        var publishers = new DictionaryTrustedPublisherStore(
            new Dictionary<string, string> { ["test-key"] = key.ExportSubjectPublicKeyInfoPem() });
        var catalog = new ExtensionCatalog(_root);
        var installer = new ExtensionInstaller(new ExtensionPackageValidator(publishers), catalog);
        await installer.InstallAsync(package);

        var registry = new VisualEffectRegistry();
        var loader = new ExtensionContentLoader(
            catalog, registry, new UiThemeManager(), new FakeShaderProbe("uRedline"));
        await loader.ReloadAsync();

        Assert.True(registry.TryGet(effectId, "1.0.0", out VisualEffectDescriptor registered));
        Assert.Equal(VisualEffectRole.Generator, registered.Role);
        Assert.Equal("uRedline", registered.Parameters[0].Uniform);
        // The shader path was rewritten to the on-disk install location.
        Assert.True(File.Exists(registered.ShaderPath));
    }

    [Fact]
    public async Task GeneratorPack_IsRejected_WhenProbeFindsAMissingUniform()
    {
        const string packageId = "com.example.meters";
        var descriptors = new[]
        {
            new VisualEffectDescriptor(
                "com.example.meters/vu", "1.0.0", packageId, "shaders/vu.frag",
                new[] { new VisualEffectParameter("redline", "uRedline", 0, 1, 0.85) },
                Role: VisualEffectRole.Generator),
        };
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string package = CreatePackage(key, packageId, new Dictionary<string, byte[]>
        {
            ["visual-effects.json"] = JsonSerializer.SerializeToUtf8Bytes(descriptors, JsonOptions),
            ["shaders/vu.frag"] = "void main(){}"u8.ToArray(),
        });
        var publishers = new DictionaryTrustedPublisherStore(
            new Dictionary<string, string> { ["test-key"] = key.ExportSubjectPublicKeyInfoPem() });
        var catalog = new ExtensionCatalog(_root);
        await new ExtensionInstaller(new ExtensionPackageValidator(publishers), catalog).InstallAsync(package);

        var registry = new VisualEffectRegistry();
        // Probe reports the shader has NO uniforms → the declared uRedline is missing → effect rejected.
        var loader = new ExtensionContentLoader(catalog, registry, new UiThemeManager(), new FakeShaderProbe());
        await loader.ReloadAsync();

        Assert.False(registry.TryGet("com.example.meters/vu", "1.0.0", out _));
    }

    private string CreatePackage(ECDsa key, string packageId, IReadOnlyDictionary<string, byte[]> files)
    {
        string path = Path.Combine(_root, $"{Guid.NewGuid():N}.liveolator-pack");
        ExtensionFile[] declared = files.Select(pair => new ExtensionFile(
            pair.Key, Convert.ToHexString(SHA256.HashData(pair.Value)), pair.Value.Length)).ToArray();
        var manifest = new ExtensionManifest(
            packageId, "1.0.0", "1.0.0", "Example",
            ExtensionContentKind.VisualEffects, Array.Empty<ExtensionDependency>(), declared);
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);

        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "manifest.json", manifestBytes);
        foreach ((string filePath, byte[] contents) in files)
            WriteEntry(archive, filePath, contents);
        byte[] signature = key.SignData(manifestBytes, HashAlgorithmName.SHA256);
        WriteEntry(archive, "signature.json", JsonSerializer.SerializeToUtf8Bytes(
            new { PublisherKeyId = "test-key", Signature = Convert.ToBase64String(signature) }, JsonOptions));
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] contents)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        using Stream stream = entry.Open();
        stream.Write(contents);
    }
}
