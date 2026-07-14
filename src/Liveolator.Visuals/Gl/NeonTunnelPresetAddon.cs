using Liveolator.Core.Visuals;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// Built-in controllable preset (doc 28): <b>TUNNEL</b> — an endless flight down a neon grid tunnel.
/// A polar-projected grid rushes toward the viewer, twisting with depth; the bass thickens the grid
/// lines, the palette shifts along the depth and the mids, and every beat fires a bright shockwave
/// ring flying up the tunnel. Knobs: SPEED / TWIST / GLOW / GRID / HUE.
/// </summary>
/// <remarks>ASCII-only shader on purpose (Intel "pre-mature EOF"); no frame feedback needed.</remarks>
public static class NeonTunnelPresetAddon
{
    public const string PackageId = "liveolator.builtin.tunnel";
    public const string EffectId = PackageId + "/generator";
    public const string PresetId = PackageId + "/preset";
    public const string Version = "1.0.0";

    public const string FragmentShader = """
        #version 330 core
        in vec2 vTexCoord;
        out vec4 fragColor;

        uniform vec2 uResolution;
        uniform float uTime;
        uniform float uBeatPhase;
        uniform float uBeatFlash;
        uniform float uLevel;
        uniform float uBass;
        uniform float uMid;

        uniform float uSpeed; // flight speed
        uniform float uTwist; // tunnel twist with depth
        uniform float uGlow;  // line brightness
        uniform float uGrid;  // grid density (segments around the tube)
        uniform float uHue;   // palette base

        vec3 palette(float t) {
            return 0.5 + 0.5 * cos(6.28318 * (t + vec3(0.00, 0.33, 0.67)));
        }

        void main() {
            vec2 p = vTexCoord * 2.0 - 1.0;
            p.x *= uResolution.x / max(uResolution.y, 1.0);
            float r = length(p) + 0.0001;
            float a = atan(p.y, p.x);
            float t = uTime * (0.3 + clamp(uSpeed, 0.0, 2.0) * 1.2);

            // Polar tunnel projection: small radius = far away; twist grows with depth.
            float depth = 0.35 / r + t * 2.0;
            a += depth * clamp(uTwist, 0.0, 1.0) * 0.35;

            float sides = 8.0 + floor(clamp(uGrid, 0.0, 1.0) * 10.0);
            float ga = abs(fract(a / 6.28318 * sides) - 0.5) * 2.0;
            float gz = abs(fract(depth) - 0.5) * 2.0;
            // The bass momentarily thickens the glowing lines.
            float edge = 0.90 - uBass * 0.06;
            float grid = max(smoothstep(edge, 1.0, ga), smoothstep(edge, 1.0, gz));

            // Beat shockwave: a bright ring launched down the tunnel each beat.
            float pulse = uBeatFlash
                * exp(-8.0 * abs(fract(depth * 0.5 - uBeatPhase) - 0.5));

            vec3 neon = palette(uHue + depth * 0.05 + uMid * 0.30);
            float fade = smoothstep(0.02, 0.35, r); // the far end vanishes into black
            vec3 col = neon * (grid * (0.55 + clamp(uGlow, 0.0, 2.0) * 0.45) + pulse * 0.8) * fade;
            col += neon * 0.10 * fade * (0.3 + uLevel); // ambient wall glow

            col = col / (1.0 + col * 0.40);
            fragColor = vec4(col, 1.0);
        }
        """;

    public static VisualEffectDescriptor Descriptor(string shaderPath) => new(
        EffectId,
        Version,
        PackageId,
        shaderPath,
        new[]
        {
            new VisualEffectParameter("speed", "uSpeed", 0.0, 2.0, 1.0),
            new VisualEffectParameter("twist", "uTwist", 0.0, 1.0, 0.3),
            new VisualEffectParameter("glow", "uGlow", 0.0, 2.0, 1.0),
            new VisualEffectParameter("grid", "uGrid", 0.0, 1.0, 0.5),
            new VisualEffectParameter("hue", "uHue", 0.0, 1.0, 0.5),
        },
        Role: VisualEffectRole.Generator);

    public static GeneratorPreset Preset() => new(
        PresetId,
        "TUNNEL",
        EffectId,
        Version,
        new[]
        {
            new ControllableParameter("speed", "SPEED"),
            new ControllableParameter("twist", "TWIST"),
            new ControllableParameter("glow", "GLOW"),
            new ControllableParameter("grid", "GRID"),
            new ControllableParameter("hue", "HUE"),
        });

    public static bool TryRegister(
        IVisualEffectRegistry effects,
        IGeneratorPresetRegistry presets,
        Action<string>? onWarning = null)
        => BuiltInPresetAddonRegistration.TryRegister(
            PackageId, "TUNNEL", "tunnel.frag", FragmentShader, Descriptor, Preset(),
            effects, presets, onWarning);
}
