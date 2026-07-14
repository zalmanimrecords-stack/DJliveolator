using System.Text.Json;
using Liveolator.Core.Visuals;
using Liveolator.Media;
using Liveolator.Media.Visuals;
using Liveolator.Mcp.Contracts;
using Microsoft.Extensions.Logging;

namespace Liveolator.Mcp.Session;

/// <summary>
/// Lets an MCP agent author FRKTL visual presets (doc 29): it owns the FRKTL presets folder (shared with
/// the app, derived from the server's data directory), validates + writes a supplied <c>.frktl</c> JSON,
/// lists what exists, and serves the authoring spec. A thin adapter over <see cref="FrktlPresetWriter"/>;
/// all validation lives in Core (<see cref="FrktlPresetValidator"/>).
/// </summary>
public sealed class VisualPresetSession
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly FrktlPresetWriter _writer;
    private readonly ILogger<VisualPresetSession> _logger;

    public VisualPresetSession(ServerConfig config, ILogger<VisualPresetSession> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        string folder = Path.Combine(config.DataDirectory ?? JsonCatalogStore.DefaultRoot(), "frktl-presets");
        _writer = new FrktlPresetWriter(folder);
        _logger = logger;
    }

    public string Folder => _writer.Folder;

    /// <summary>Parses, validates, and writes a <c>.frktl</c> JSON document. A parse/validation error is returned, not thrown.</summary>
    public VisualPresetResult Create(string presetJson, bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(presetJson))
            return new VisualPresetResult(false, null, null, "Preset JSON is empty.");

        FrktlPresetFile? file;
        try
        {
            file = JsonSerializer.Deserialize<FrktlPresetFile>(presetJson, ReadOptions);
        }
        catch (JsonException ex)
        {
            return new VisualPresetResult(false, null, null, $"Preset JSON could not be parsed ({ex.Message}).");
        }

        if (file is null)
            return new VisualPresetResult(false, null, null, "Preset JSON deserialized to nothing.");

        FrktlPresetWriteResult result = _writer.Write(file, overwrite);
        if (result.Created)
            _logger.LogInformation("Agent created FRKTL preset '{PresetId}' at {Path}.", result.PresetId, result.Path);
        else
            _logger.LogInformation("Agent FRKTL preset create rejected: {Error}", result.Error);
        return VisualPresetResult.From(result);
    }

    public IReadOnlyList<VisualPresetSummary> List()
        => _writer.List().Select(VisualPresetSummary.From).ToList();

    public VisualPresetSpec Spec()
        => new(Folder, GeneratorPreset.MaxControllableParameters, FrktlPresetAuthoring.Guide, FrktlPresetAuthoring.ExampleJson);
}
