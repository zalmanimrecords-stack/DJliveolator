using Liveolator.Core.Visuals;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// Built-in reference for the controllable-preset standard (doc 28): <b>FRKTL</b>, a full-frame generator
/// that samples the previous frame (<c>uPreviousFrame</c>) for frame-feedback trails/warp, layered with
/// audio- and beat-reactive energy. It registers both the generator effect and a <see cref="GeneratorPreset"/>
/// that exposes exactly five controllable parameters (GLOW / WARP / SPEED / ZOOM / DECAY), each drivable
/// live from a UI knob or an external controller via <c>VisualSetMacro</c>.
/// </summary>
/// <remarks>
/// The shader is ASCII-only on purpose — non-ASCII bytes trip some GL preprocessors ("premature EOF" on
/// Intel); <c>ShaderText.Sanitize</c> is the backstop. Feedback is engaged automatically because the
/// shader declares <c>uPreviousFrame</c> (see <see cref="GeneratorPass"/>).
/// </remarks>
public static class FrktlPresetAddon
{
    public const string PackageId = "liveolator.builtin.frktl";
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
        uniform float uBarPhase;
        uniform float uBeatFlash;
        uniform float uLevel;
        uniform float uPeak;
        uniform float uBass;
        uniform float uLowMid;
        uniform float uMid;
        uniform float uHigh;

        uniform sampler2D uPreviousFrame;

        uniform float uGlow;   // brightness of newly injected energy
        uniform float uWarp;   // feedback rotation / swirl amount
        uniform float uSpeed;  // animation speed
        uniform float uZoom;   // feedback zoom (trail scale toward centre)
        uniform float uDecay;  // trail persistence (how slowly the previous frame fades)

        const float PI = 3.14159265359;

        vec3 palette(float t) {
            return 0.5 + 0.5 * cos(6.28318 * (t + vec3(0.00, 0.33, 0.67)));
        }

        void main() {
            // --- Feedback: sample last frame at a zoomed + swirled coordinate, then fade it. ---
            vec2 centred = vTexCoord - 0.5;
            float zoom = 1.0 - (0.010 + clamp(uZoom, 0.0, 1.0) * 0.045);
            float swirl = clamp(uWarp, 0.0, 1.0) * 0.06 * sin(uTime * 0.3 + length(centred) * 6.28318);
            float cs = cos(swirl);
            float sn = sin(swirl);
            mat2 rot = mat2(cs, -sn, sn, cs);
            vec2 prevUv = rot * centred * zoom + 0.5;
            float decay = clamp(uDecay, 0.80, 0.995);
            vec3 prev = texture(uPreviousFrame, prevUv).rgb * decay;

            // --- New energy: kaleidoscopic petals + beat shockwave, tinted by the spectrum. ---
            vec2 p = vTexCoord * 2.0 - 1.0;
            p.x *= uResolution.x / max(uResolution.y, 1.0);
            float t = uTime * (0.2 + clamp(uSpeed, 0.0, 2.0) * 0.8);
            float r = length(p);
            float a = atan(p.y, p.x);

            float petals = 6.0;
            float k = abs(sin(a * petals + t + uLowMid * 3.0));
            float petalMask = smoothstep(0.45, 1.0, k) * smoothstep(1.05, 0.15, r);
            float ring = 0.5 + 0.5 * sin(r * 11.0 - t * 2.2 + uBass * 6.0);
            float energy = petalMask * (0.35 + ring * 0.65);

            // Beat shockwave that expands across the beat.
            float waveR = 0.20 + clamp(uBeatPhase, 0.0, 1.0) * 0.62;
            energy += uBeatFlash * smoothstep(0.07, 0.0, abs(r - waveR));

            vec3 neon = palette(a / 6.28318 + t * 0.05 + uMid * 0.4 + uBarPhase * 0.1);
            float drive = (0.30 + clamp(uGlow, 0.0, 2.0) * 0.70) * (0.35 + uLevel * 1.3 + uPeak * 0.2);
            vec3 add = neon * energy * drive;
            add += neon * uHigh * 0.25 * petalMask;

            // Compose feedback + new energy, soft-limit to keep neon punch without harsh white clipping.
            vec3 col = prev + add;
            col = col / (1.0 + col * 0.5);
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
            new VisualEffectParameter("glow", "uGlow", 0.0, 2.0, 1.0),
            new VisualEffectParameter("warp", "uWarp", 0.0, 1.0, 0.4),
            new VisualEffectParameter("speed", "uSpeed", 0.0, 2.0, 1.0),
            new VisualEffectParameter("zoom", "uZoom", 0.0, 1.0, 0.5),
            new VisualEffectParameter("decay", "uDecay", 0.80, 0.995, 0.95),
        },
        Role: VisualEffectRole.Generator);

    /// <summary>The FRKTL preset exposing all five generator parameters as controllable knobs (doc 28).</summary>
    public static GeneratorPreset Preset() => new(
        PresetId,
        "FRKTL",
        EffectId,
        Version,
        new[]
        {
            new ControllableParameter("glow", "GLOW"),
            new ControllableParameter("warp", "WARP"),
            new ControllableParameter("speed", "SPEED"),
            new ControllableParameter("zoom", "ZOOM"),
            new ControllableParameter("decay", "DECAY"),
        });

    public static string EnsureShaderCreated(string? directory = null)
        => BuiltInPresetAddonRegistration.EnsureShaderCreated("frktl.frag", FragmentShader, directory);

    /// <summary>
    /// Registers the generator effect and its preset. Both share the package id so an uninstall/reload
    /// removes them together. Failure to write the shader degrades to a warning and leaves the registries
    /// untouched (never crashes composition — doc 08 rule).
    /// </summary>
    public static bool TryRegister(
        IVisualEffectRegistry effects,
        IGeneratorPresetRegistry presets,
        Action<string>? onWarning = null)
        => BuiltInPresetAddonRegistration.TryRegister(
            PackageId, "FRKTL", "frktl.frag", FragmentShader, Descriptor, Preset(),
            effects, presets, onWarning);
}
