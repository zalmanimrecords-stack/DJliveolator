namespace Liveolator.Mcp.Session;

/// <summary>
/// Static agent-facing authoring content for control skins (doc 30), served by <c>get_control_skin_spec</c>:
/// the parametric <c>.ctrlskin</c> format guide and a complete example an agent can adapt. A control skin is
/// pure data (colours, no bitmap) — the app renders the existing vector knob/slider with the palette.
/// </summary>
internal static class ControlSkinAuthoring
{
    public const string Guide = """
        A control skin is a single JSON object (a .ctrlskin file) describing the LOOK of one performer
        control as a colour palette — no image needed. The app renders its built-in vector knob/slider
        with these colours. Keys:
          name        (string, required) - shown in the skin picker
          author      (string, optional)
          description (string, optional, one line)
          kind        (string, required) - "Knob" or "Slider" (case-insensitive)
          accent      (string, required) - the active arc (knob) / fill (slider) colour; the one signal colour
          track       (string, optional) - the inactive groove / track colour
          pointer     (string, optional) - the pointer (knob) / value-mark colour
          body        (string, optional) - the cap / moving-part body colour

        HARD RULES (a file that breaks any is rejected, and nothing is written):
          - name is non-empty; kind is "Knob" or "Slider".
          - accent is required. Every colour present must be "#RRGGBB" or "#AARRGGBB" hex.
          - Omitted optional colours fall back to the control's defaults, so accent-only is valid.

        GUIDANCE:
          - Pick ONE saturated accent (the signal colour) and keep track/body dark and desaturated so the
            accent reads at a glance on a dark stage UI. Pointer is usually near-white for contrast.
          - Author a matching pair (a Knob and a Slider sharing the same accent) for a coherent surface.

        Create the skin by calling create_control_skin with the whole JSON object as a string.
        """;

    public const string ExampleJson = """
        {
          "name": "Cobalt Knob",
          "author": "Liveolator",
          "description": "Deep navy cap with a single cobalt-blue signal arc.",
          "kind": "Knob",
          "accent": "#2F80F6",
          "track": "#26303F",
          "pointer": "#E7ECF3",
          "body": "#12171F"
        }
        """;
}
