using Liveolator.Core.Visuals;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// The built-in analog VU-meter generator — the first reference visual add-on (doc 26). It is shipped
/// in-app so a generator layer renders and reacts to the live master level out of the box; the very same
/// shader + a package manifest is the canonical third-party add-on documented in doc 26.
/// </summary>
/// <remarks>
/// The fragment shader draws itself purely from uniforms (no input texture) — it is a
/// <see cref="VisualEffectRole.Generator"/>. The needle swings with the smoothed <c>uLevel</c> (VU
/// ballistics resolved in Core), a peak dot rides the arc from <c>uPeak</c>, and the scale turns red past
/// the <c>uRedline</c> parameter. The shader file is emitted to the app's asset cache like
/// <see cref="StarterImage"/>; a write failure degrades to "no built-in generator" rather than crashing.
/// </remarks>
public static class VuMeterAddon
{
    /// <summary>The built-in add-on's package id (kept distinct from any installable extension).</summary>
    public const string PackageId = "liveolator.builtin";

    /// <summary>The generator effect id a scene layer references via <see cref="VisualSourceKind.Generator"/>.</summary>
    public const string EffectId = "liveolator.builtin/vu-meter";

    public const string Version = "1.0.0";

    // Analog VU meter drawn from uniforms. Contract per doc 26: #version 330 core, in vec2 vTexCoord,
    // out vec4 fragColor; automatic uniforms uResolution/uLevel/uRms/uPeak; one declared parameter
    // uRedline. No texture sampling — a generator produces its own pixels.
    public const string FragmentShader = """
        #version 330 core
        in vec2 vTexCoord;
        out vec4 fragColor;

        uniform vec2  uResolution;
        uniform float uLevel;    // smoothed VU level 0..1 (the needle)
        uniform float uRms;      // raw RMS 0..1 (unused here, available to forks)
        uniform float uPeak;     // raw peak 0..1 (the peak dot)
        uniform float uRedline;  // scale fraction where the arc turns red

        // Distance from point p to segment a-b.
        float sdSegment(vec2 p, vec2 a, vec2 b) {
            vec2 pa = p - a, ba = b - a;
            float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
            return length(pa - ba * h);
        }

        void main() {
            // Aspect-corrected space, y up. vTexCoord.y runs top→bottom, so flip it.
            vec2 uv = vec2(vTexCoord.x, 1.0 - vTexCoord.y);
            float aspect = uResolution.x / max(uResolution.y, 1.0);
            vec2 p = vec2((uv.x - 0.5) * aspect, uv.y);

            // Warm cream panel with a soft vignette, evoking an analog meter face.
            vec3 col = vec3(0.91, 0.87, 0.75);
            float vign = smoothstep(1.3, 0.25, length(vec2((uv.x - 0.5) * aspect, uv.y - 0.5)));
            col *= mix(0.80, 1.0, vign);

            vec2  pivot  = vec2(0.0, 0.06);
            float radius = 0.62;
            float minAng = radians(140.0);  // level 0 → up-left
            float maxAng = radians(40.0);   // level 1 → up-right

            vec2  d   = p - pivot;
            float r   = length(d);
            float ang = atan(d.y, d.x);
            bool  inArc = ang <= minAng && ang >= maxAng;
            float t   = clamp((minAng - ang) / (minAng - maxAng), 0.0, 1.0); // 0 left → 1 right

            // Scale arc, red past the redline.
            if (inArc) {
                float band = smoothstep(0.014, 0.0, abs(r - radius));
                vec3 scaleCol = t < uRedline ? vec3(0.11) : vec3(0.78, 0.10, 0.08);
                col = mix(col, scaleCol, band);

                // Tick marks just inside the arc.
                float ticks = abs(fract(t * 10.0 + 0.5) - 0.5) * 2.0;
                float mark = smoothstep(0.05, 0.0, ticks) * smoothstep(0.05, 0.0, abs(r - (radius - 0.05)));
                col = mix(col, scaleCol, mark * 0.85);
            }

            // Needle.
            float needleAng = mix(minAng, maxAng, clamp(uLevel, 0.0, 1.0));
            vec2  tip = pivot + vec2(cos(needleAng), sin(needleAng)) * (radius + 0.02);
            col = mix(col, vec3(0.06), smoothstep(0.013, 0.004, sdSegment(p, pivot, tip)));

            // Hub.
            col = mix(col, vec3(0.10), smoothstep(0.05, 0.032, length(p - pivot)));

            // Peak dot riding the arc.
            float pkAng = mix(minAng, maxAng, clamp(uPeak, 0.0, 1.0));
            vec2  pkPos = pivot + vec2(cos(pkAng), sin(pkAng)) * radius;
            col = mix(col, vec3(0.86, 0.10, 0.07), smoothstep(0.022, 0.0, length(p - pkPos)));

            fragColor = vec4(col, 1.0);
        }
        """;

    /// <summary>Builds the descriptor for the built-in generator, pointing at the emitted shader file.</summary>
    public static VisualEffectDescriptor Descriptor(string shaderPath) => new(
        EffectId,
        Version,
        PackageId,
        shaderPath,
        new[] { new VisualEffectParameter("redline", "uRedline", Min: 0.0, Max: 1.0, Default: 0.85) },
        Role: VisualEffectRole.Generator);

    /// <summary>
    /// Ensures the shader exists and returns its absolute path. Idempotent: writes only when missing.
    /// Throws on a genuine write failure — callers that want best-effort startup should guard the call.
    /// </summary>
    public static string EnsureShaderCreated(string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Liveolator", "assets");
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "vu-meter.frag");
        // Rewrite when missing or stale so a shipped shader update reaches an existing install.
        if (!File.Exists(path) || File.ReadAllText(path) != FragmentShader)
            File.WriteAllText(path, FragmentShader);
        return path;
    }

    /// <summary>
    /// Emits the shader and registers the generator into <paramref name="registry"/> so a generator
    /// layer can reference <see cref="EffectId"/>. Best-effort: a write/registry failure logs and leaves
    /// the app running without the built-in generator (the rest of the visuals still render).
    /// </summary>
    public static bool TryRegister(IVisualEffectRegistry registry, Action<string>? onWarning = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        try
        {
            string shaderPath = EnsureShaderCreated();
            registry.ReplacePackage(PackageId, new[] { Descriptor(shaderPath) });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            onWarning?.Invoke($"Built-in VU-meter generator unavailable ({ex.Message}); visuals run without it.");
            return false;
        }
    }
}
