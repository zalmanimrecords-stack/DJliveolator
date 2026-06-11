using Liveolator.Media.Visuals;

namespace Liveolator.Mcp.Contracts;

/// <summary>Result of an agent's attempt to create a FRKTL preset (doc 29): created + its id/path, or an error.</summary>
public sealed record VisualPresetResult(bool Created, string? PresetId, string? Path, string? Error)
{
    public static VisualPresetResult From(FrktlPresetWriteResult result)
        => new(result.Created, result.PresetId, result.Path, result.Error);
}

/// <summary>One existing FRKTL preset on disk, for an agent listing what is installed.</summary>
public sealed record VisualPresetSummary(string Name, string PresetId, string Path)
{
    public static VisualPresetSummary From(FrktlPresetEntry entry)
        => new(entry.Name, entry.PresetId, entry.Path);
}

/// <summary>
/// The authoring contract an agent needs to write a valid FRKTL preset (doc 29): a prose guide, the exact
/// folder presets are written to, the parameter ceiling, and a complete working example it can adapt.
/// </summary>
public sealed record VisualPresetSpec(string FolderPath, int MaxParameters, string Guide, string ExampleJson);
