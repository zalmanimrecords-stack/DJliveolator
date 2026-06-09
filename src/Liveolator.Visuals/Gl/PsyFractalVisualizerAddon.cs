using Liveolator.Core.Visuals;

namespace Liveolator.Visuals.Gl;

/// <summary>Built-in audio-reactive psytrance fractal mandala generator.</summary>
public static class PsyFractalVisualizerAddon
{
    public const string PackageId = "liveolator.builtin.psy-fractal";
    public const string EffectId = PackageId + "/visualizer";
    public const string Version = "1.0.0";

    public const string FragmentShader = """
        #version 330 core
        in vec2 vTexCoord;
        out vec4 fragColor;

        uniform vec2 uResolution;
        uniform float uTime;
        uniform float uBeatPhase;
        uniform float uBarPhase;
        uniform float uBeatFlash;
        uniform float uLevel;
        uniform float uPeak;
        uniform float uBass;
        uniform float uLowMid;
        uniform float uMid;
        uniform float uHigh;

        uniform float uSensitivity;
        uniform float uGlow;
        uniform float uComplexity;
        uniform float uSymmetry;
        uniform float uSpeed;
        uniform float uPalette;
        uniform float uReducedMotion;
        uniform float uQuality;

        const float PI = 3.14159265359;

        float line(float d, float width) {
            return exp(-max(d, 0.0) * max(d, 0.0) / max(width * width, 0.000001));
        }

        vec3 palette(float t, float preset) {
            vec3 a;
            vec3 b;
            vec3 c;
            if (preset < 0.5) {
                a = vec3(0.00, 0.95, 1.00);
                b = vec3(0.95, 0.05, 0.85);
                c = vec3(0.45, 1.00, 0.08);
            } else if (preset < 1.5) {
                a = vec3(0.10, 1.00, 0.35);
                b = vec3(0.00, 0.85, 0.80);
                c = vec3(0.55, 0.12, 0.95);
            } else if (preset < 2.5) {
                a = vec3(1.00, 0.25, 0.02);
                b = vec3(1.00, 0.75, 0.05);
                c = vec3(0.55, 0.02, 0.75);
            } else {
                a = vec3(0.05, 0.35, 1.00);
                b = vec3(0.25, 0.05, 0.75);
                c = vec3(1.00, 0.20, 0.75);
            }
            float wave = 0.5 + 0.5 * cos(6.28318 * (t + vec3(0.00, 0.33, 0.67)));
            return mix(mix(a, b, wave.x), c, wave.y * 0.62);
        }

        float hash21(vec2 p) {
            p = fract(p * vec2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return fract(p.x * p.y);
        }

        void main() {
            vec2 uv = vTexCoord * 2.0 - 1.0;
            uv.y *= -1.0;
            uv.x *= uResolution.x / max(uResolution.y, 1.0);

            float sensitivity = clamp(uSensitivity, 0.2, 2.5);
            float bass = clamp(uBass * sensitivity + uLevel * 0.18, 0.0, 1.0);
            float lowMid = clamp(uLowMid * sensitivity, 0.0, 1.0);
            float mid = clamp(uMid * sensitivity, 0.0, 1.0);
            float high = clamp(uHigh * sensitivity + uPeak * 0.08, 0.0, 1.0);
            float motion = mix(1.0, 0.16, clamp(uReducedMotion, 0.0, 1.0));
            float time = uTime * uSpeed * motion;

            float r = length(uv);
            float angle = atan(uv.y, uv.x);
            float symmetry = floor(clamp(uSymmetry, 6.0, 32.0) + 0.5);
            float sector = 2.0 * PI / symmetry;
            float folded = abs(mod(angle + sector * 0.5, sector) - sector * 0.5);
            float spoke = folded / sector;

            float beatRipple = uBeatFlash * (1.0 - clamp(uBeatPhase, 0.0, 1.0));
            float breathing = 0.015 * sin(time * 1.2 + uBarPhase * 2.0 * PI);
            float coreRadius = 0.25 + bass * 0.075 + breathing;
            float deformation = sin(angle * symmetry + time * 0.8) * (0.012 + lowMid * 0.026);

            float energy = 0.0;
            float colorIndex = 0.0;

            // Recursive sacred-geometry rings.
            for (int i = 0; i < 7; i++) {
                float enabled = step(float(i), 2.0 + floor(clamp(uComplexity, 0.0, 1.0) * 4.0));
                float fi = float(i);
                float radius = coreRadius + fi * 0.065 + deformation * (1.0 + fi * 0.15);
                float teeth = sin(angle * symmetry * (1.0 + mod(fi, 3.0)) + time * (0.3 + fi * 0.07));
                float d = abs(r - radius - teeth * (0.006 + mid * 0.008));
                float ring = line(d, 0.006 + bass * 0.004) * enabled;
                energy += ring * (0.72 + 0.12 * fi);
                colorIndex += ring * (0.08 * fi + time * 0.025);
            }

            // Mirrored tribal branches and sharp sigil tips.
            float branchCenter = 0.38 + lowMid * 0.035;
            float branchCurve = abs(spoke - (0.17 + 0.22 * sin(r * 18.0 - time)));
            float branchMask = smoothstep(0.30, 0.05, branchCurve);
            float branchRange = (1.0 - smoothstep(branchCenter, 0.69, r)) * smoothstep(0.19, 0.31, r);
            float branch = branchMask * branchRange;
            float spike = line(abs(spoke) * r, 0.010 + lowMid * 0.012)
                        * smoothstep(0.72, 0.22, r) * smoothstep(0.18, 0.32, r);
            energy += branch * (0.75 + lowMid) + spike * (0.45 + mid);
            colorIndex += branch * (0.45 + spoke);

            // Beat shockwave.
            float waveRadius = 0.30 + clamp(uBeatPhase, 0.0, 1.0) * 0.58;
            float shockwave = line(abs(r - waveRadius), 0.009) * beatRipple;
            energy += shockwave * 1.8;

            // Orbiting shards and high-frequency particles.
            float particleCount = mix(18.0, 52.0, clamp(uQuality, 0.0, 1.0));
            float cellAngle = floor((angle + PI) / (2.0 * PI) * particleCount);
            float rnd = hash21(vec2(cellAngle, floor(time * 0.7)));
            float orbit = 0.62 + 0.13 * sin(cellAngle * 1.7 + time * (0.7 + rnd));
            orbit += bass * 0.055;
            float angularDot = abs(fract((angle + PI) / (2.0 * PI) * particleCount) - 0.5);
            float particle = line(abs(r - orbit), 0.007 + high * 0.006)
                           * line(angularDot, 0.09)
                           * (0.15 + high * 1.6);
            energy += particle;
            colorIndex += particle * rnd;

            // Elegant deep-space field.
            vec2 starCell = floor((uv + 2.0) * 70.0);
            float star = step(0.992 - high * 0.004, hash21(starCell))
                       * (0.08 + high * 0.32)
                       * (1.0 - smoothstep(0.0, 0.012, length(fract((uv + 2.0) * 70.0) - 0.5)));

            vec3 base = vec3(0.002, 0.003, 0.012);
            float halo = exp(-r * r * 3.4) * (0.035 + bass * 0.12);
            vec3 neon = palette(colorIndex + angle / (2.0 * PI) + time * 0.018, floor(uPalette + 0.5));
            float glow = clamp(uGlow, 0.0, 1.5);
            vec3 col = base + neon * energy * (0.65 + glow * 0.8);
            col += neon * pow(max(energy, 0.0), 2.0) * glow * 0.34;
            col += palette(0.62, floor(uPalette + 0.5)) * halo;
            col += vec3(0.35, 0.55, 1.0) * star;

            // Soft limiter avoids harsh white flashing while retaining neon punch.
            col = col / (1.0 + col * 0.42);
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
            new VisualEffectParameter("sensitivity", "uSensitivity", 0.2, 2.5, 1.0),
            new VisualEffectParameter("glow", "uGlow", 0.0, 1.5, 0.85),
            new VisualEffectParameter("complexity", "uComplexity", 0.0, 1.0, 0.75),
            new VisualEffectParameter("symmetry", "uSymmetry", 6.0, 32.0, 16.0),
            new VisualEffectParameter("speed", "uSpeed", 0.0, 2.5, 1.0),
            new VisualEffectParameter("palette", "uPalette", 0.0, 3.0, 0.0),
            new VisualEffectParameter("reduced-motion", "uReducedMotion", 0.0, 1.0, 0.0),
            new VisualEffectParameter("quality", "uQuality", 0.0, 1.0, 1.0),
        },
        Role: VisualEffectRole.Generator);

    public static string EnsureShaderCreated(string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Liveolator",
            "assets");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "psy-fractal-visualizer.frag");
        if (!File.Exists(path) || File.ReadAllText(path) != FragmentShader)
            File.WriteAllText(path, FragmentShader);
        return path;
    }

    public static bool TryRegister(IVisualEffectRegistry registry, Action<string>? onWarning = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        try
        {
            registry.ReplacePackage(PackageId, new[] { Descriptor(EnsureShaderCreated()) });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            onWarning?.Invoke($"Built-in Psy Fractal generator unavailable ({ex.Message}).");
            return false;
        }
    }
}
