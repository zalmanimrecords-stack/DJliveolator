using System.Text.Json;
using Liveolator.Core.Skins;
using Liveolator.Media;
using Liveolator.Media.Skins;
using Liveolator.Mcp.Contracts;
using Microsoft.Extensions.Logging;

namespace Liveolator.Mcp.Session;

/// <summary>
/// Lets an MCP agent author parametric control skins (doc 30): it owns the control-skins folder (shared with
/// the app, derived from the server's data directory), validates + writes a supplied <c>.ctrlskin</c> JSON,
/// lists what exists, and serves the authoring spec. A thin adapter over <see cref="ControlSkinWriter"/>;
/// all validation lives in Core (<see cref="ControlSkinValidator"/>). Mirrors <see cref="VisualPresetSession"/>.
/// </summary>
public sealed class ControlSkinSession
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ControlSkinWriter _writer;
    private readonly ILogger<ControlSkinSession> _logger;

    public ControlSkinSession(ServerConfig config, ILogger<ControlSkinSession> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        string folder = Path.Combine(config.DataDirectory ?? JsonCatalogStore.DefaultRoot(), "control-skins");
        _writer = new ControlSkinWriter(folder);
        _logger = logger;
    }

    public string Folder => _writer.Folder;

    /// <summary>Parses, validates, and writes a <c>.ctrlskin</c> JSON document. A parse/validation error is returned, not thrown.</summary>
    public ControlSkinResult Create(string skinJson, bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(skinJson))
            return new ControlSkinResult(false, null, null, "Skin JSON is empty.");

        ControlSkinFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ControlSkinFile>(skinJson, ReadOptions);
        }
        catch (JsonException ex)
        {
            return new ControlSkinResult(false, null, null, $"Skin JSON could not be parsed ({ex.Message}).");
        }

        if (file is null)
            return new ControlSkinResult(false, null, null, "Skin JSON deserialized to nothing.");

        ControlSkinWriteResult result = _writer.Write(file, overwrite);
        if (result.Created)
            _logger.LogInformation("Agent created control skin '{SkinId}' at {Path}.", result.SkinId, result.Path);
        else
            _logger.LogInformation("Agent control skin create rejected: {Error}", result.Error);
        return ControlSkinResult.From(result);
    }

    public IReadOnlyList<ControlSkinSummary> List()
        => _writer.List().Select(ControlSkinSummary.From).ToList();

    public ControlSkinSpec Spec()
        => new(Folder, ControlSkinKind.All, ControlSkinAuthoring.Guide, ControlSkinAuthoring.ExampleJson);
}
