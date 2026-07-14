using Liveolator.Mcp;
using Liveolator.Mcp.Contracts;
using Liveolator.Mcp.Session;
using Liveolator.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Mcp.Tests;

/// <summary>
/// The FRKTL preset authoring tools (doc 29): an agent fetches the spec, creates a preset from .frktl
/// JSON (validated + written to the shared folder), and lists what exists. Uses an isolated data dir.
/// </summary>
public sealed class VisualPresetToolsTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"liveolator-mcp-preset-tests-{Guid.NewGuid():N}");

    private VisualPresetSession NewSession()
        => new(new ServerConfig { DataDirectory = _directory }, NullLogger<VisualPresetSession>.Instance);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void GetVisualPresetSpec_ReturnsAnExampleThatItselfCreatesSuccessfully()
    {
        VisualPresetSession session = NewSession();
        VisualPresetSpec spec = VisualTools.GetVisualPresetSpec(session);

        Assert.Equal(5, spec.MaxParameters);
        Assert.Contains("frktl-presets", spec.FolderPath);
        Assert.Contains("uPreviousFrame", spec.Guide);

        // The example the agent is given must be valid input to create_visual_preset.
        VisualPresetResult created = VisualTools.CreateVisualPreset(session, spec.ExampleJson);
        Assert.True(created.Created, created.Error);
        Assert.Equal("liveolator.frktl.user/aurora-veil", created.PresetId);
    }

    [Fact]
    public void CreateVisualPreset_WritesAValidPreset_AndListShowsIt()
    {
        VisualPresetSession session = NewSession();
        const string json = """
            {
              "name": "Tunnel Pulse",
              "parameters": [
                { "id": "glow", "uniform": "uGlow", "label": "GLOW", "min": 0.0, "max": 2.0, "default": 1.0 }
              ],
              "shader": "#version 330 core\nin vec2 vTexCoord;\nout vec4 fragColor;\nuniform float uGlow;\nvoid main(){ fragColor = vec4(uGlow); }"
            }
            """;

        VisualPresetResult result = VisualTools.CreateVisualPreset(session, json);

        Assert.True(result.Created, result.Error);
        Assert.Equal("liveolator.frktl.user/tunnel-pulse", result.PresetId);

        VisualPresetSummary listed = Assert.Single(VisualTools.ListVisualPresets(session));
        Assert.Equal("Tunnel Pulse", listed.Name);
        Assert.Equal(result.PresetId, listed.PresetId);
    }

    [Fact]
    public void CreateVisualPreset_ReturnsError_ForInvalidShader_WithoutWriting()
    {
        VisualPresetSession session = NewSession();
        const string json = """
            { "name": "Broken", "parameters": [], "shader": "totally not glsl" }
            """;

        VisualPresetResult result = VisualTools.CreateVisualPreset(session, json);

        Assert.False(result.Created);
        Assert.NotNull(result.Error);
        Assert.Empty(VisualTools.ListVisualPresets(session));
    }

    [Fact]
    public void CreateVisualPreset_ReturnsError_ForMalformedJson()
    {
        VisualPresetResult result = VisualTools.CreateVisualPreset(NewSession(), "{ not json");
        Assert.False(result.Created);
        Assert.NotNull(result.Error);
    }
}
