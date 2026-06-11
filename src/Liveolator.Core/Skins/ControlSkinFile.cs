namespace Liveolator.Core.Skins;

/// <summary>
/// The on-disk shape of a parametric control skin (doc 30): a named look for one performer control —
/// a rotary <c>Knob</c> or a <c>Slider</c>/fader — described purely by colours, not a bitmap. An agent
/// authors it via MCP (<c>create_control_skin</c>); the app renders the existing vector control with this
/// palette, so no image asset is required. Kept a plain record (no throwing constructor) so tolerant
/// loading can validate then skip a bad file rather than crash — see <see cref="ControlSkinValidator"/>.
/// Colours are <c>#RRGGBB</c> or <c>#AARRGGBB</c>; only <see cref="Accent"/> is required (the active
/// arc/fill), the rest fall back to the control's defaults when omitted.
/// </summary>
public sealed record ControlSkinFile
{
    public string Name { get; init; } = string.Empty;
    public string? Author { get; init; }
    public string? Description { get; init; }

    /// <summary>Which control this skin styles: <c>Knob</c> or <c>Slider</c> (see <see cref="ControlSkinKind"/>).</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Required. The active arc (knob) / fill (slider) colour — the one signal colour.</summary>
    public string Accent { get; init; } = string.Empty;

    /// <summary>Optional. The inactive groove / track colour.</summary>
    public string? Track { get; init; }

    /// <summary>Optional. The pointer (knob) / value mark colour.</summary>
    public string? Pointer { get; init; }

    /// <summary>Optional. The cap / body colour of the moving part.</summary>
    public string? Body { get; init; }
}
