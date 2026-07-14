using Liveolator.Core.Visuals;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// Built-in controllable preset (doc 28): <b>STORM</b> — a stormy sea under a rolling cloud deck.
/// FBM waves swell with the bass, wind drives both the chop and the cloud scroll, foam breaks on the
/// crests with the highs, and lightning strikes the horizon on the beat — a jagged bolt plus a full-sky
/// flash whose amount is the STORM knob. Knobs: WAVES / WIND / STORM / SPEED / HUE.
/// </summary>
/// <remarks>ASCII-only shader on purpose (Intel "pre-mature EOF"); no frame feedback needed.</remarks>
public static class StormySeaPresetAddon
{
    public const string PackageId = "liveolator.builtin.storm";
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

        uniform float uWaves; // swell height
        uniform float uWind;  // chop + cloud scroll
        uniform float uStorm; // lightning amount
        uniform float uSpeed; // overall pace
        uniform float uHue;   // water / sky tint

        const float HORIZON = 0.52;

        float hash11(float p) {
            p = fract(p * 0.1031);
            p *= p + 33.33;
            return fract(p * (p + p));
        }

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

        vec3 tint(float t) {
            return 0.5 + 0.5 * cos(6.28318 * (t + vec3(0.00, 0.12, 0.28)));
        }

        void main() {
            vec2 uv = vTexCoord;
            float aspect = uResolution.x / max(uResolution.y, 1.0);
            float t = uTime * (0.3 + clamp(uSpeed, 0.0, 2.0) * 0.7);
            float wind = 0.3 + clamp(uWind, 0.0, 2.0) * 0.85;
            float flash = uBeatFlash * clamp(uStorm, 0.0, 1.0);

            vec3 waterDeep = tint(uHue) * vec3(0.10, 0.16, 0.22);
            vec3 waterLit = tint(uHue + 0.05) * vec3(0.25, 0.38, 0.45);
            vec3 cloudDark = tint(uHue + 0.45) * 0.16 + 0.04;
            vec3 cloudLit = tint(uHue + 0.50) * 0.38 + 0.10;

            vec3 col;
            if (uv.y > HORIZON) {
                // Rolling cloud deck, scrolled by the wind and torn by a second FBM octave.
                vec2 cp = vec2(uv.x * 3.0 * aspect + t * wind, (uv.y - HORIZON) * 4.5);
                float cloud = fbm(cp + fbm(cp * 1.7 - t * wind * 0.4) * 0.9);
                col = mix(cloudDark, cloudLit, cloud);
                col *= 0.55 + 0.45 * smoothstep(HORIZON, 1.0, uv.y);

                // Lightning: a jagged bolt from the cloud base, position re-rolled per strike.
                float strike = floor(uTime * 1.3);
                float bx = 0.15 + hash11(strike) * 0.7;
                float wob = (noise2(vec2(uv.y * 9.0, strike * 3.7)) - 0.5) * 0.12;
                float boltX = bx + wob * (1.0 - uv.y);
                float bolt = smoothstep(0.006, 0.0, abs(uv.x - boltX))
                    * smoothstep(1.0, HORIZON, uv.y);
                col += vec3(0.85, 0.90, 1.0) * flash * (bolt * 2.2 + 0.35 * cloud);
            } else {
                // Perspective-projected wave field: swell from the knob, breathing with the bass.
                float d = HORIZON - uv.y;
                float persp = 1.0 / (d + 0.02);
                float wx = (uv.x - 0.5) * persp * aspect;
                float wz = persp * 0.35 + t * 2.0;
                float swell = (0.3 + clamp(uWaves, 0.0, 1.0) * 0.7) * (1.0 + uBass * 0.5);
                float h = fbm(vec2(wx * 1.5 + t * wind, wz)) * swell;

                col = mix(waterDeep, waterLit, h);
                // Foam breaking on the crests; spray sparkle rides the highs.
                float foam = smoothstep(0.72, 0.86, h + noise2(vec2(wx * 6.0, wz * 3.0)) * 0.12);
                col += vec3(0.75, 0.82, 0.85) * foam * (0.5 + uHigh * 0.9 + uLevel * 0.3);
                // Distance haze toward the horizon + the lightning lighting the water.
                float haze = smoothstep(0.30, 0.0, d);
                col = mix(col, cloudDark + 0.05, haze * 0.6);
                col += vec3(0.55, 0.62, 0.75) * flash * (0.25 + h * 0.35);
            }

            col = col / (1.0 + col * 0.30);
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
            new VisualEffectParameter("waves", "uWaves", 0.0, 1.0, 0.65),
            new VisualEffectParameter("wind", "uWind", 0.0, 2.0, 1.0),
            new VisualEffectParameter("storm", "uStorm", 0.0, 1.0, 0.7),
            new VisualEffectParameter("speed", "uSpeed", 0.0, 2.0, 1.0),
            new VisualEffectParameter("hue", "uHue", 0.0, 1.0, 0.55),
        },
        Role: VisualEffectRole.Generator);

    public static GeneratorPreset Preset() => new(
        PresetId,
        "STORM",
        EffectId,
        Version,
        new[]
        {
            new ControllableParameter("waves", "WAVES"),
            new ControllableParameter("wind", "WIND"),
            new ControllableParameter("storm", "STORM"),
            new ControllableParameter("speed", "SPEED"),
            new ControllableParameter("hue", "HUE"),
        });

    public static bool TryRegister(
        IVisualEffectRegistry effects,
        IGeneratorPresetRegistry presets,
        Action<string>? onWarning = null)
        => BuiltInPresetAddonRegistration.TryRegister(
            PackageId, "STORM", "storm.frag", FragmentShader, Descriptor, Preset(),
            effects, presets, onWarning);
}
