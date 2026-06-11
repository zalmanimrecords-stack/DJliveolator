namespace Liveolator.Mcp.Session;

/// <summary>
/// Static agent-facing authoring content for FRKTL presets (doc 29), served by
/// <c>get_visual_preset_spec</c>: the format/contract guide and a complete example an agent can adapt.
/// Kept ASCII-only — the shader inside a <c>.frktl</c> must be ASCII (some GL drivers choke otherwise).
/// </summary>
internal static class FrktlPresetAuthoring
{
    public const string Guide = """
        A FRKTL preset is a single JSON object (a .frktl file). Keys:
          name        (string, required) - shown in the preset picker
          author      (string, optional)
          description (string, optional, one line)
          parameters  (array, 0..5)      - each becomes a live, MIDI-mappable knob:
                        { "id": string, "uniform": string, "label": string,
                          "min": number, "max": number, "default": number }
          shader      (string, required) - a complete GLSL "#version 330 core" fragment shader

        HARD RULES (a file that breaks any is rejected):
          - At most 5 parameters; unique id and unique uniform each; non-empty label (UPPERCASE, <=6 chars);
            max >= min; min <= default <= max. Parameter uniforms should start with "u" (e.g. "uGlow").
          - The shader MUST be ASCII-only (no smart quotes / em dashes / accents, even in comments),
            start with "#version 330 core", declare `in vec2 vTexCoord;` and `out vec4 fragColor;`,
            contain `void main()`, write `fragColor`, and declare a `uniform float` for every parameter.
          - Write an opaque colour: fragColor = vec4(rgb, 1.0). Keep rgb ~0..1; a soft limiter
            `col = col / (1.0 + col);` avoids white clipping.

        HOST UNIFORMS (set automatically every frame; declare only those you use):
          uniform vec2  uResolution;    // pixels
          uniform float uTime;          // seconds
          uniform float uBeatPhase;     // 0..1 within a beat
          uniform float uBarPhase;      // 0..1 within a bar
          uniform float uConfidence;    // 0..1 beat-detection confidence
          uniform float uBeatFlash;     // decaying pulse, peaks on the beat
          uniform float uLevel;         // 0..1 smoothed master level (best for motion)
          uniform float uRms, uPeak;    // 0..1 raw energy / peak
          uniform float uBass, uLowMid, uMid, uHigh; // 0..1 spectrum bands
          uniform sampler2D uPreviousFrame; // OPTIONAL: declare to get the previous frame for trails/warp

        GUIDANCE:
          - vTexCoord is 0..1 (y top->bottom). Centered math: vec2 p = vTexCoord*2.0 - 1.0; then
            p.x *= uResolution.x / max(uResolution.y, 1.0);
          - React to music (uLevel/uBass for brightness/scale, uBeatFlash for pulses) and animate with uTime.
          - For MilkDrop-style trails: declare uPreviousFrame, sample it at a slightly zoomed/rotated
            coordinate, multiply by ~0.9-0.97 decay, then add new energy on top.
          - Expose 3-5 expressive knobs (GLOW, SPEED, WARP/ZOOM, DECAY, a COLOR/HUE shift) and wire each
            declared uniform into the look so turning it has a visible effect.

        Create the preset by calling create_visual_preset with the whole JSON object as a string.
        """;

    public const string ExampleJson = """
        {
          "name": "Aurora Veil",
          "author": "Liveolator",
          "description": "Drifting neon veil with feedback trails that bloom on the bass.",
          "parameters": [
            { "id": "glow",  "uniform": "uGlow",  "label": "GLOW",  "min": 0.0, "max": 2.0,   "default": 1.0 },
            { "id": "speed", "uniform": "uSpeed", "label": "SPEED", "min": 0.0, "max": 2.0,   "default": 0.8 },
            { "id": "warp",  "uniform": "uWarp",  "label": "WARP",  "min": 0.0, "max": 1.0,   "default": 0.5 },
            { "id": "decay", "uniform": "uDecay", "label": "DECAY", "min": 0.8, "max": 0.985, "default": 0.94 }
          ],
          "shader": "#version 330 core\nin vec2 vTexCoord;\nout vec4 fragColor;\nuniform vec2 uResolution;\nuniform float uTime;\nuniform float uBeatFlash;\nuniform float uLevel;\nuniform float uBass;\nuniform float uMid;\nuniform sampler2D uPreviousFrame;\nuniform float uGlow;\nuniform float uSpeed;\nuniform float uWarp;\nuniform float uDecay;\nvoid main(){\n  vec2 c = vTexCoord - 0.5;\n  float ang = uWarp * 0.05 * sin(uTime * 0.3 + length(c) * 6.2831);\n  float cs = cos(ang), sn = sin(ang);\n  vec2 prevUv = mat2(cs,-sn,sn,cs) * c * (1.0 - 0.02) + 0.5;\n  vec3 prev = texture(uPreviousFrame, prevUv).rgb * clamp(uDecay, 0.8, 0.985);\n  vec2 p = vTexCoord * 2.0 - 1.0;\n  p.x *= uResolution.x / max(uResolution.y, 1.0);\n  float t = uTime * (0.2 + uSpeed * 0.8);\n  float r = length(p);\n  float a = atan(p.y, p.x);\n  float wave = 0.5 + 0.5 * sin(a * 6.0 + t + uMid * 3.0);\n  float band = smoothstep(0.9, 0.2, r) * wave;\n  band += uBeatFlash * smoothstep(0.06, 0.0, abs(r - (0.2 + uBass * 0.4)));\n  vec3 neon = 0.5 + 0.5 * cos(6.2831 * (vec3(0.0,0.33,0.67) + a/6.2831 + t*0.05));\n  vec3 add = neon * band * (0.3 + uGlow * 0.7) * (0.4 + uLevel * 1.3);\n  vec3 col = prev + add;\n  col = col / (1.0 + col * 0.5);\n  fragColor = vec4(col, 1.0);\n}\n"
        }
        """;
}
