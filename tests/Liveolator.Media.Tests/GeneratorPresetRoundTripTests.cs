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
/// Proves the controllable-preset standard (doc 28) end-to-end for a third-party pack: a signed
/// <c>.liveolator-pack</c> carrying a generator <c>visual-effects.json</c> plus a <c>presets.json</c>
/// installs, and <see cref="ExtensionContentLoader"/> validates each preset against its generator
/// descriptor and registers it. A preset that exposes an undeclared parameter is rejected (and the
/// whole pack's presets skipped) without aborting the load.
/// </summary>
public sealed class GeneratorPresetRoundTripTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private const string PackageId = "com.example.vis";
    private const string GeneratorId = "com.example.vis/generator";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "liveolator-preset-addon-tests", Guid.NewGuid().ToString("N"));

    // One key signs the package and seeds the trusted-publisher store, so installs validate.
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public GeneratorPresetRoundTripTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        _key.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class FakeShaderProbe : IVisualShaderProbe
    {
        private readonly string[] _uniforms;
        public FakeShaderProbe(params string[] uniforms) => _uniforms = uniforms;

        public Task<VisualShaderProbeResult> ProbeAsync(string shaderPath, CancellationToken cancellationToken = default)
            => Task.FromResult(new VisualShaderProbeResult(File.Exists(shaderPath), null, _uniforms));
    }

    private static VisualEffectDescriptor[] GeneratorDescriptor() => new[]
    {
        new VisualEffectDescriptor(
            GeneratorId, "1.0.0", PackageId, "shaders/generator.frag",
            new[]
            {
                new VisualEffectParameter("glow", "uGlow", 0, 1, 0.5),
                new VisualEffectParameter("warp", "uWarp", 0, 4, 1.0),
            },
            Role: VisualEffectRole.Generator),
    };

    private static byte[] Shader() =>
        "#version 330 core\nuniform float uGlow;\nuniform float uWarp;\nout vec4 fragColor;\nvoid main(){fragColor=vec4(uGlow,uWarp,0.0,1.0);}"u8.ToArray();

    [Fact]
    public async Task PresetPack_InstallsAndRegistersPreset()
    {
        var presets = new[]
        {
            new GeneratorPreset(
                "com.example.vis/aurora", "Aurora", GeneratorId, "1.0.0",
                new[] { new ControllableParameter("glow", "GLOW"), new ControllableParameter("warp", "WARP") }),
        };

        string package = CreatePackage(new Dictionary<string, byte[]>
        {
            ["visual-effects.json"] = JsonSerializer.SerializeToUtf8Bytes(GeneratorDescriptor(), JsonOptions),
            ["presets.json"] = JsonSerializer.SerializeToUtf8Bytes(presets, JsonOptions),
            ["shaders/generator.frag"] = Shader(),
        });

        var (effects, presetRegistry) = await LoadPackageAsync(package, new FakeShaderProbe("uGlow", "uWarp"));

        Assert.True(effects.TryGet(GeneratorId, "1.0.0", out _));
        Assert.True(presetRegistry.TryGet("com.example.vis/aurora", out GeneratorPreset registered));
        Assert.Equal("Aurora", registered.Name);
        Assert.Equal(2, registered.Controllable.Count);
    }

    [Fact]
    public async Task Preset_IsRejected_WhenItExposesAnUndeclaredParameter()
    {
        var presets = new[]
        {
            new GeneratorPreset(
                "com.example.vis/bad", "Bad", GeneratorId, "1.0.0",
                new[] { new ControllableParameter("nonexistent", "NOPE") }),
        };

        string package = CreatePackage(new Dictionary<string, byte[]>
        {
            ["visual-effects.json"] = JsonSerializer.SerializeToUtf8Bytes(GeneratorDescriptor(), JsonOptions),
            ["presets.json"] = JsonSerializer.SerializeToUtf8Bytes(presets, JsonOptions),
            ["shaders/generator.frag"] = Shader(),
        });

        var (effects, presetRegistry) = await LoadPackageAsync(package, new FakeShaderProbe("uGlow", "uWarp"));

        // The generator still registers; only the invalid preset set is skipped.
        Assert.True(effects.TryGet(GeneratorId, "1.0.0", out _));
        Assert.False(presetRegistry.TryGet("com.example.vis/bad", out _));
        Assert.Empty(presetRegistry.Presets);
    }

    [Fact]
    public async Task Preset_IsRejected_WhenItReferencesAnUnknownGenerator()
    {
        var presets = new[]
        {
            new GeneratorPreset(
                "com.example.vis/orphan", "Orphan", "com.example.vis/missing", "1.0.0",
                new[] { new ControllableParameter("glow", "GLOW") }),
        };

        string package = CreatePackage(new Dictionary<string, byte[]>
        {
            ["visual-effects.json"] = JsonSerializer.SerializeToUtf8Bytes(GeneratorDescriptor(), JsonOptions),
            ["presets.json"] = JsonSerializer.SerializeToUtf8Bytes(presets, JsonOptions),
            ["shaders/generator.frag"] = Shader(),
        });

        var (_, presetRegistry) = await LoadPackageAsync(package, new FakeShaderProbe("uGlow", "uWarp"));

        Assert.Empty(presetRegistry.Presets);
    }

    private async Task<(IVisualEffectRegistry Effects, IGeneratorPresetRegistry Presets)> LoadPackageAsync(
        string package, IVisualShaderProbe probe)
    {
        var publishers = new DictionaryTrustedPublisherStore(
            new Dictionary<string, string> { ["test-key"] = _key.ExportSubjectPublicKeyInfoPem() });
        var catalog = new ExtensionCatalog(_root);
        await new ExtensionInstaller(new ExtensionPackageValidator(publishers), catalog).InstallAsync(package);

        var effects = new VisualEffectRegistry();
        var presets = new GeneratorPresetRegistry();
        var loader = new ExtensionContentLoader(
            catalog, effects, new UiThemeManager(), probe, presets: presets);
        await loader.ReloadAsync();
        return (effects, presets);
    }

    private string CreatePackage(IReadOnlyDictionary<string, byte[]> files)
    {
        string path = Path.Combine(_root, $"{Guid.NewGuid():N}.liveolator-pack");
        ExtensionFile[] declared = files.Select(pair => new ExtensionFile(
            pair.Key, Convert.ToHexString(SHA256.HashData(pair.Value)), pair.Value.Length)).ToArray();
        var manifest = new ExtensionManifest(
            PackageId, "1.0.0", "1.0.0", "Example",
            ExtensionContentKind.VisualEffects, Array.Empty<ExtensionDependency>(), declared);
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);

        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "manifest.json", manifestBytes);
        foreach ((string filePath, byte[] contents) in files)
            WriteEntry(archive, filePath, contents);
        byte[] signature = _key.SignData(manifestBytes, HashAlgorithmName.SHA256);
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
