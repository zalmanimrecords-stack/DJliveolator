using Liveolator.Core.Visuals;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// Built-in controllable preset (doc 28): <b>SIGIL</b> — ever-morphing mystical symbols. A glowing
/// sigil built from a polygon ring, an inner star, concentric circles and runic ticks continuously
/// crossfades into the next randomly-drawn sigil (side counts, star order, spin and tick pattern all
/// re-rolled per symbol) while the palette cycles; it breathes with the bass and flares on the beat.
/// Knobs: MORPH / GLOW / SPEED / DETAIL / HUE.
/// </summary>
/// <remarks>ASCII-only shader on purpose (Intel "pre-mature EOF"); no frame feedback needed.</remarks>
public static class MysticSigilsPresetAddon
{
    public const string PackageId = "liveolator.builtin.sigil";
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
        uniform float uMid;

        uniform float uMorph;  // how fast one sigil becomes the next
        uniform float uGlow;   // line glow width
        uniform float uSpeed;  // rotation speed
        uniform float uDetail; // rings + runic ticks amount
        uniform float uHue;    // palette base

        const float PI = 3.14159265359;
        const float TAU = 6.28318530718;

        float hash11(float p) {
            p = fract(p * 0.1031);
            p *= p + 33.33;
            return fract(p * (p + p));
        }

        vec3 palette(float t) {
            return 0.5 + 0.5 * cos(TAU * (t + vec3(0.00, 0.33, 0.67)));
        }

        // Polar radius of a regular n-gon at angle a (1.0 = circumradius).
        float polyR(float a, float n) {
            float seg = TAU / n;
            return cos(seg * 0.5) / cos(mod(a, seg) - seg * 0.5);
        }

        float ringGlow(float d, float w) {
            return w / (abs(d) + w);
        }

        // The glowing line energy of one sigil, drawn from its random seed.
        float sigil(float r, float a, float R, float seed, float glowW, float detail) {
            float n1 = 3.0 + floor(hash11(seed * 7.13) * 5.0);  // outer polygon: 3..7 sides
            float n2 = 3.0 + floor(hash11(seed * 3.77) * 6.0);  // inner star order
            float aa = a * ((hash11(seed * 9.31) > 0.5) ? 1.0 : -1.0)
                + hash11(seed * 5.03) * TAU;

            float e = ringGlow(r - R * polyR(aa, n1), glowW);
            e += ringGlow(r - R * (0.42 + 0.20 * cos(aa * n2)), glowW) * 0.9;
            e += ringGlow(r - R * 1.12, glowW) * (0.3 + detail * 0.7) * 0.7;
            e += ringGlow(r - R * 0.22, glowW) * 0.8;

            // Runic ticks: a random on/off pattern around the 0.86R circle.
            float m = 8.0 + floor(hash11(seed * 5.51) * 9.0);
            float sector = floor(aa / TAU * m);
            float ticked = step(0.35, hash11(seed * 13.7 + sector));
            float da = abs(mod(aa, TAU / m) - PI / m);
            e += smoothstep(0.05, 0.01, da) * smoothstep(0.10, 0.02, abs(r - R * 0.86))
                * ticked * detail * 1.4;
            return e;
        }

        void main() {
            vec2 p = vTexCoord * 2.0 - 1.0;
            p.x *= uResolution.x / max(uResolution.y, 1.0);
            float r = length(p);
            float a = atan(p.y, p.x) + uTime * (0.1 + clamp(uSpeed, 0.0, 2.0) * 0.35);

            // Symbol clock: MORPH sets how fast the current sigil crossfades into the next.
            float s = uTime * (0.05 + clamp(uMorph, 0.0, 2.0) * 0.20);
            float seedA = floor(s);
            float blend = smoothstep(0.15, 0.85, fract(s));

            float breathe = 1.0 + 0.05 * sin(uTime * 0.8) + uBeatFlash * 0.08 + uBass * 0.10;
            float R = 0.55 * breathe;
            float glowW = 0.003 + clamp(uGlow, 0.0, 2.0) * 0.006;
            float detail = clamp(uDetail, 0.0, 1.0);

            float e = sigil(r, a, R, seedA, glowW, detail) * (1.0 - blend)
                + sigil(r, a, R, seedA + 1.0, glowW, detail) * blend;

            // Color cycles with time, the mids and the radius, so shape AND color keep shifting.
            vec3 lineCol = palette(uHue + uTime * 0.03 + uMid * 0.30 + r * 0.15);
            vec3 col = lineCol * e * (0.55 + uLevel * 0.8 + uBeatFlash * 0.4);
            col += palette(uHue + 0.5) * 0.030 * max(1.0 - r, 0.0); // faint altar haze

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
            new VisualEffectParameter("morph", "uMorph", 0.0, 2.0, 1.0),
            new VisualEffectParameter("glow", "uGlow", 0.0, 2.0, 1.0),
            new VisualEffectParameter("speed", "uSpeed", 0.0, 2.0, 1.0),
            new VisualEffectParameter("detail", "uDetail", 0.0, 1.0, 0.6),
            new VisualEffectParameter("hue", "uHue", 0.0, 1.0, 0.0),
        },
        Role: VisualEffectRole.Generator);

    public static GeneratorPreset Preset() => new(
        PresetId,
        "SIGIL",
        EffectId,
        Version,
        new[]
        {
            new ControllableParameter("morph", "MORPH"),
            new ControllableParameter("glow", "GLOW"),
            new ControllableParameter("speed", "SPEED"),
            new ControllableParameter("detail", "DETAIL"),
            new ControllableParameter("hue", "HUE"),
        });

    public static bool TryRegister(
        IVisualEffectRegistry effects,
        IGeneratorPresetRegistry presets,
        Action<string>? onWarning = null)
        => BuiltInPresetAddonRegistration.TryRegister(
            PackageId, "SIGIL", "sigil.frag", FragmentShader, Descriptor, Preset(),
            effects, presets, onWarning);
}
