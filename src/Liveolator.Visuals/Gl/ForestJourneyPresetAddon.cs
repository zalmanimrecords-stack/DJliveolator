using Liveolator.Core.Visuals;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// Built-in controllable preset (doc 28): <b>FOREST</b> — an endless journey through a misty forest.
/// Five parallax conifer treelines scroll past at depth-dependent speeds (near layers fast, far layers
/// slow), fog swallows the distant layers, a low sun glows on the horizon and pulses with the beat and
/// the bass, and fireflies sparkle between the near trees with the highs.
/// Knobs: SPEED / FOG / LIGHT / TREES / HUE.
/// </summary>
/// <remarks>ASCII-only shader on purpose (Intel "pre-mature EOF"); no frame feedback needed.</remarks>
public static class ForestJourneyPresetAddon
{
    public const string PackageId = "liveolator.builtin.forest";
    public const string EffectId = PackageId + "/generator";
    public const string PresetId = PackageId + "/preset";
    public const string Version = "1.0.0";

    public const string FragmentShader = """
        #version 330 core
        in vec2 vTexCoord;
        out vec4 fragColor;

        uniform vec2 uResolution;
        uniform float uTime;
        uniform float uBeatFlash;
        uniform float uLevel;
        uniform float uBass;
        uniform float uHigh;

        uniform float uSpeed;  // travel speed
        uniform float uFog;    // mist density on the far layers
        uniform float uLight;  // sun glow strength
        uniform float uTrees;  // treeline density / jaggedness
        uniform float uHue;    // forest tint

        float hash11(float p) {
            p = fract(p * 0.1031);
            p *= p + 33.33;
            return fract(p * (p + p));
        }

        float noise1(float x) {
            float i = floor(x);
            float f = fract(x);
            float u = f * f * (3.0 - 2.0 * f);
            return mix(hash11(i), hash11(i + 1.0), u);
        }

        // Jagged conifer treeline height for one parallax layer.
        float treeline(float x, float seed) {
            float dens = 0.35 + clamp(uTrees, 0.0, 1.0) * 0.65;
            float h = noise1(x * 1.7 + seed * 19.0) * 0.20;
            h += pow(noise1(x * 6.0 + seed * 31.0), 2.0) * 0.22 * dens;
            h += pow(noise1(x * 16.0 + seed * 53.0), 4.0) * 0.14 * dens;
            return h;
        }

        vec3 tint(float t) {
            return 0.5 + 0.5 * cos(6.28318 * (t + vec3(0.10, 0.35, 0.62)));
        }

        void main() {
            vec2 uv = vTexCoord;
            float aspect = uResolution.x / max(uResolution.y, 1.0);
            float t = uTime * (0.2 + clamp(uSpeed, 0.0, 2.0) * 0.8);
            uv.y += sin(t * 2.3) * 0.006; // gentle walking bob

            // Sky: pre-dawn gradient with a low sun that pulses on the beat and the bass.
            vec3 skyHi = tint(uHue + 0.52) * 0.25;
            vec3 skyLo = tint(uHue + 0.08) * 0.55;
            vec3 col = mix(skyLo, skyHi, smoothstep(0.25, 1.0, uv.y));
            vec2 sunPos = vec2(0.5, 0.34);
            float sunD = length((uv - sunPos) * vec2(aspect, 1.0));
            float glow = (0.35 + clamp(uLight, 0.0, 2.0) * 0.65)
                * (1.0 + uBeatFlash * 0.8 + uBass * 0.4);
            vec3 sunCol = tint(uHue + 0.03) + 0.35;
            col += sunCol * glow * 0.55 * exp(-sunD * 5.0);
            col += sunCol * glow * 0.12 * exp(-sunD * 1.6);

            vec3 fogCol = mix(skyLo, sunCol, 0.25);
            float fog = 0.25 + clamp(uFog, 0.0, 1.0) * 0.75;

            // Five treelines, far to near: nearer layers scroll faster and rise out of the fog.
            for (int i = 0; i < 5; i++) {
                float depth = float(i) / 4.0; // 0 = farthest
                float seed = float(i) * 7.7 + 3.1;
                float scroll = t * mix(0.12, 1.1, depth);
                float x = uv.x * aspect * mix(1.5, 4.0, depth) + scroll;
                float base = mix(0.34, 0.02, depth);
                float h = base + treeline(x, seed) * mix(0.6, 1.35, depth);
                float m = smoothstep(h + 0.004, h - 0.004, uv.y);
                vec3 layerCol = tint(uHue) * mix(0.30, 0.035, depth);
                layerCol = mix(layerCol, fogCol, fog * (1.0 - depth) * 0.85);
                col = mix(col, layerCol, m);
            }

            // Fireflies between the near trees, excited by the highs.
            vec2 g = uv * vec2(aspect, 1.0) * 22.0 + vec2(t * 0.9, 0.0);
            vec2 cell = floor(g);
            float fh = hash11(cell.x * 12.9898 + cell.y * 78.233);
            vec2 fp = fract(g) - vec2(hash11(fh * 91.0), hash11(fh * 47.0));
            float twinkle = 0.5 + 0.5 * sin(uTime * (2.0 + fh * 5.0) + fh * 40.0);
            float fly = step(0.94, fh) * exp(-dot(fp, fp) * 60.0) * twinkle;
            col += vec3(0.75, 1.0, 0.45) * fly
                * (0.25 + uHigh * 1.4 + uLevel * 0.4) * step(uv.y, 0.55);

            col = col / (1.0 + col * 0.35);
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
            new VisualEffectParameter("fog", "uFog", 0.0, 1.0, 0.5),
            new VisualEffectParameter("light", "uLight", 0.0, 2.0, 1.0),
            new VisualEffectParameter("trees", "uTrees", 0.0, 1.0, 0.6),
            new VisualEffectParameter("hue", "uHue", 0.0, 1.0, 0.30),
        },
        Role: VisualEffectRole.Generator);

    public static GeneratorPreset Preset() => new(
        PresetId,
        "FOREST",
        EffectId,
        Version,
        new[]
        {
            new ControllableParameter("speed", "SPEED"),
            new ControllableParameter("fog", "FOG"),
            new ControllableParameter("light", "LIGHT"),
            new ControllableParameter("trees", "TREES"),
            new ControllableParameter("hue", "HUE"),
        });

    public static bool TryRegister(
        IVisualEffectRegistry effects,
        IGeneratorPresetRegistry presets,
        Action<string>? onWarning = null)
        => BuiltInPresetAddonRegistration.TryRegister(
            PackageId, "FOREST", "forest.frag", FragmentShader, Descriptor, Preset(),
            effects, presets, onWarning);
}
