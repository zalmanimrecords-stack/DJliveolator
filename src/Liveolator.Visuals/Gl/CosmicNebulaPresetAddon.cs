using Liveolator.Core.Visuals;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// Built-in controllable preset (doc 28): <b>NEBULA</b> — a slowly drifting deep-space nebula.
/// Domain-warped FBM clouds swirl through a cycling palette, swelling and brightening with the bass
/// and the beat, while two scales of stars twinkle through the thin regions with the highs.
/// Knobs: DRIFT / CLOUD / STARS / GLOW / HUE.
/// </summary>
/// <remarks>ASCII-only shader on purpose (Intel "pre-mature EOF"); no frame feedback needed.</remarks>
public static class CosmicNebulaPresetAddon
{
    public const string PackageId = "liveolator.builtin.nebula";
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

        uniform float uDrift; // cloud drift speed
        uniform float uCloud; // nebula density
        uniform float uStars; // star amount
        uniform float uGlow;  // nebula brightness
        uniform float uHue;   // palette base

        float hash21(vec2 p) {
            return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
        }

        float noise2(vec2 p) {
            vec2 i = floor(p);
            vec2 f = fract(p);
            vec2 u = f * f * (3.0 - 2.0 * f);
            return mix(mix(hash21(i), hash21(i + vec2(1.0, 0.0)), u.x),
                       mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), u.x), u.y);
        }

        float fbm(vec2 p) {
            float v = 0.0;
            float a = 0.5;
            for (int i = 0; i < 5; i++) {
                v += a * noise2(p);
                p = p * 2.03 + vec2(17.0, 9.0);
                a *= 0.5;
            }
            return v;
        }

        vec3 palette(float t) {
            return 0.5 + 0.5 * cos(6.28318 * (t + vec3(0.00, 0.33, 0.67)));
        }

        // One scale of hash-grid stars, twinkling per-star.
        float stars(vec2 uv, float scale, float threshold) {
            vec2 g = uv * scale;
            vec2 cell = floor(g);
            float h = hash21(cell);
            vec2 sp = fract(g) - vec2(hash21(cell + 11.1), hash21(cell + 27.7));
            float twinkle = 0.5 + 0.5 * sin(uTime * (1.0 + h * 6.0) + h * 40.0);
            return step(threshold, h) * exp(-dot(sp, sp) * 90.0) * twinkle;
        }

        void main() {
            float aspect = uResolution.x / max(uResolution.y, 1.0);
            vec2 uv = vTexCoord * vec2(aspect, 1.0);
            float t = uTime * (0.01 + clamp(uDrift, 0.0, 2.0) * 0.03);

            // Domain-warped FBM: q warps p, r warps the warp - the classic living-cloud look.
            vec2 p = uv * 3.0;
            vec2 q = vec2(fbm(p + t), fbm(p + vec2(5.2, 1.3) - t));
            vec2 r = vec2(fbm(p + 2.2 * q + vec2(1.7, 9.2) + t * 1.6),
                          fbm(p + 2.2 * q + vec2(8.3, 2.8)));
            float n = fbm(p + 2.5 * r);

            float density = clamp(uCloud, 0.0, 1.0);
            float shape = smoothstep(0.35 - density * 0.25, 0.95, n);

            vec3 cA = palette(uHue);
            vec3 cB = palette(uHue + 0.15);
            vec3 cC = palette(uHue + 0.45);
            vec3 cloud = mix(cA, cB, clamp(n * 1.6, 0.0, 1.0));
            cloud = mix(cloud, cC, clamp(q.x * 1.3, 0.0, 1.0));

            float drive = (0.30 + clamp(uGlow, 0.0, 2.0) * 0.55)
                * (0.6 + uBass * 0.9 + uBeatFlash * 0.35 + uLevel * 0.3);
            vec3 col = cloud * shape * drive;

            // Stars shine through where the nebula is thin; the highs excite the twinkle.
            float starAmt = clamp(uStars, 0.0, 1.0);
            float s = stars(uv, 26.0, 0.93) + stars(uv + 3.7, 60.0, 0.96) * 0.6;
            col += vec3(0.80, 0.85, 1.0) * s * starAmt
                * (0.5 + uHigh * 1.2) * (1.0 - shape * 0.7);

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
            new VisualEffectParameter("drift", "uDrift", 0.0, 2.0, 0.5),
            new VisualEffectParameter("cloud", "uCloud", 0.0, 1.0, 0.6),
            new VisualEffectParameter("stars", "uStars", 0.0, 1.0, 0.5),
            new VisualEffectParameter("glow", "uGlow", 0.0, 2.0, 1.0),
            new VisualEffectParameter("hue", "uHue", 0.0, 1.0, 0.7),
        },
        Role: VisualEffectRole.Generator);

    public static GeneratorPreset Preset() => new(
        PresetId,
        "NEBULA",
        EffectId,
        Version,
        new[]
        {
            new ControllableParameter("drift", "DRIFT"),
            new ControllableParameter("cloud", "CLOUD"),
            new ControllableParameter("stars", "STARS"),
            new ControllableParameter("glow", "GLOW"),
            new ControllableParameter("hue", "HUE"),
        });

    public static bool TryRegister(
        IVisualEffectRegistry effects,
        IGeneratorPresetRegistry presets,
        Action<string>? onWarning = null)
        => BuiltInPresetAddonRegistration.TryRegister(
            PackageId, "NEBULA", "nebula.frag", FragmentShader, Descriptor, Preset(),
            effects, presets, onWarning);
}
