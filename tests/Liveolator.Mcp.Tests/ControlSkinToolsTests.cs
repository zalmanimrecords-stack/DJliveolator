using Liveolator.Mcp;
using Liveolator.Mcp.Contracts;
using Liveolator.Mcp.Session;
using Liveolator.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Mcp.Tests;

/// <summary>
/// The control-skin authoring tools (doc 30): an agent fetches the spec, creates a parametric knob/slider
/// skin from .ctrlskin JSON (validated + written to the shared folder), and lists what exists. Isolated dir.
/// </summary>
public sealed class ControlSkinToolsTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"liveolator-mcp-skin-tests-{Guid.NewGuid():N}");

    private ControlSkinSession NewSession()
        => new(new ServerConfig { DataDirectory = _directory }, NullLogger<ControlSkinSession>.Instance);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void GetControlSkinSpec_ReturnsAnExampleThatItselfCreatesSuccessfully()
    {
        ControlSkinSession session = NewSession();
        ControlSkinSpec spec = ControlSkinTools.GetControlSkinSpec(session);

        Assert.Contains("control-skins", spec.FolderPath);
        Assert.Contains("Knob", spec.Kinds);
        Assert.Contains("Slider", spec.Kinds);

        // The example the agent is given must be valid input to create_control_skin.
        ControlSkinResult created = ControlSkinTools.CreateControlSkin(session, spec.ExampleJson);
        Assert.True(created.Created, created.Error);
        Assert.Equal("liveolator.control-skins/cobalt-knob", created.SkinId);
    }

    [Fact]
    public void CreateControlSkin_WritesAValidSkin_AndListShowsIt()
    {
        ControlSkinSession session = NewSession();
        const string json = """
            { "name": "Amber Slider", "kind": "Slider", "accent": "#D78A16", "track": "#241707" }
            """;

        ControlSkinResult result = ControlSkinTools.CreateControlSkin(session, json);

        Assert.True(result.Created, result.Error);
        Assert.Equal("liveolator.control-skins/amber-slider", result.SkinId);

        ControlSkinSummary listed = Assert.Single(ControlSkinTools.ListControlSkins(session));
        Assert.Equal("Amber Slider", listed.Name);
        Assert.Equal("Slider", listed.Kind);
        Assert.Equal(result.SkinId, listed.SkinId);
    }

    [Fact]
    public void CreateControlSkin_ReturnsError_ForBadColour_WithoutWriting()
    {
        ControlSkinSession session = NewSession();
        const string json = """
            { "name": "Broken", "kind": "Knob", "accent": "blue" }
            """;

        ControlSkinResult result = ControlSkinTools.CreateControlSkin(session, json);

        Assert.False(result.Created);
        Assert.NotNull(result.Error);
        Assert.Empty(ControlSkinTools.ListControlSkins(session));
    }

    [Fact]
    public void CreateControlSkin_ReturnsError_ForMalformedJson()
    {
        ControlSkinResult result = ControlSkinTools.CreateControlSkin(NewSession(), "{ not json");
        Assert.False(result.Created);
        Assert.NotNull(result.Error);
    }
}
