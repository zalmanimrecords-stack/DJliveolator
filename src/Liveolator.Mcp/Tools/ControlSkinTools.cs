using System.ComponentModel;
using Liveolator.Mcp.Contracts;
using Liveolator.Mcp.Session;
using ModelContextProtocol.Server;

namespace Liveolator.Mcp.Tools;

/// <summary>
/// MCP tools for authoring parametric control skins (doc 30): an agent can design the LOOK of a performer
/// knob or slider as a colour palette and save it where the app picks it up — no image asset needed.
/// Validation/writing live in Core/Media; these are thin adapters over <see cref="ControlSkinSession"/>.
/// </summary>
[McpServerToolType]
public sealed class ControlSkinTools
{
    [McpServerTool(Name = "get_control_skin_spec")]
    [Description("Get the authoring contract for a control skin: the .ctrlskin JSON format (name, kind " +
                 "Knob/Slider, an accent colour plus optional track/pointer/body colours), the rules " +
                 "(accent required, colours are #RRGGBB/#AARRGGBB), the folder skins are written to, and a " +
                 "complete worked example. ALWAYS call this before create_control_skin so the JSON is valid.")]
    public static ControlSkinSpec GetControlSkinSpec(ControlSkinSession session)
        => session.Spec();

    [McpServerTool(Name = "create_control_skin")]
    [Description("Create a parametric control skin (a knob or slider LOOK described by colours) from a " +
                 "complete .ctrlskin JSON document and save it into the control-skins folder, where the app " +
                 "picks it up. The JSON must follow get_control_skin_spec (name + kind + accent + optional " +
                 "track/pointer/body colours). It is validated before writing; on failure nothing is written " +
                 "and the reason is returned in 'error'. The file name and skin id are derived from the name.")]
    public static ControlSkinResult CreateControlSkin(
        ControlSkinSession session,
        [Description("The entire .ctrlskin document as a JSON string (keys: name, author?, description?, " +
                     "kind, accent, track?, pointer?, body?). See get_control_skin_spec for the exact shape.")]
        string skinJson,
        [Description("Overwrite an existing skin file with the same derived name. Default true.")]
        bool overwrite = true)
        => session.Create(skinJson, overwrite);

    [McpServerTool(Name = "list_control_skins")]
    [Description("List the control skins currently installed in the control-skins folder (name + id + kind " +
                 "+ file path), so an agent can see what exists before creating or replacing one.")]
    public static IReadOnlyList<ControlSkinSummary> ListControlSkins(ControlSkinSession session)
        => session.List();
}
