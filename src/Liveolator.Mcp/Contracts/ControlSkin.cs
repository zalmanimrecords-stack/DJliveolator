using Liveolator.Media.Skins;

namespace Liveolator.Mcp.Contracts;

/// <summary>Result of an agent's attempt to create a control skin (doc 30): created + its id/path, or an error.</summary>
public sealed record ControlSkinResult(bool Created, string? SkinId, string? Path, string? Error)
{
    public static ControlSkinResult From(ControlSkinWriteResult result)
        => new(result.Created, result.SkinId, result.Path, result.Error);
}

/// <summary>One existing control skin on disk, for an agent listing what is installed.</summary>
public sealed record ControlSkinSummary(string Name, string SkinId, string Kind, string Path)
{
    public static ControlSkinSummary From(ControlSkinEntry entry)
        => new(entry.Name, entry.SkinId, entry.Kind, entry.Path);
}

/// <summary>
/// The authoring contract an agent needs to write a valid control skin (doc 30): a prose guide, the exact
/// folder skins are written to, the control kinds it can style, and a complete working example to adapt.
/// </summary>
public sealed record ControlSkinSpec(string FolderPath, IReadOnlyList<string> Kinds, string Guide, string ExampleJson);
