using System.Globalization;
using Liveolator.Core.Settings;
using Liveolator.Core.Visuals;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// The built-in analog VU-meter generator — the first reference visual add-on (doc 26). It is shipped
/// in-app so a generator layer renders and reacts to the live master level out of the box; the very same
/// shader + a package manifest is the canonical third-party add-on documented in doc 26.
/// </summary>
/// <remarks>
/// The look matches a real analog VU meter by splitting it the way skeuomorphic plugins do: the static
/// dial — bezel, screws, aged cream face, dual-row scale, red zone, "VU METER", brass hub — is a PNG
/// rendered by <see cref="VuMeterFace"/> (an image layer), and this generator draws ONLY the moving
/// needle on a transparent background over it. The needle is a <see cref="VisualEffectRole.Generator"/>
/// driven purely by <c>uLevel</c> (VU ballistics resolved in Core); it shares <see cref="VuMeterGeometry"/>
/// with the face so the needle registers with the printed arc. The shader file is emitted to the app's
/// asset cache like <see cref="StarterImage"/>; a write failure degrades to "no built-in generator".
/// </remarks>
public static class VuMeterAddon
{
    /// <summary>The built-in add-on's package id (kept distinct from any installable extension).</summary>
    public const string PackageId = "liveolator.builtin";

    /// <summary>The generator effect id a scene layer references via <see cref="VisualSourceKind.Generator"/>.</summary>
    public const string EffectId = "liveolator.builtin/vu-meter";

    public const string Version = "1.0.0";

    // The self-contained VU-meter generator: it samples the dial-FACE background image (uBackground —
    // the built-in VuMeterFace by default, or the user's custom image set from the Add-ons tab) and draws
    // the moving needle OVER it, so a single generator layer is the whole meter (no separate face layer
    // needed). Contract per doc 26: #version 330 core, in vec2 vTexCoord, out vec4 fragColor; uLevel
    // drives the needle angle. It works in FACE PIXEL SPACE using the shared VuMeterGeometry so the needle
    // aligns with the printed arc. Built from the geometry constants so there is one source of truth.
    public static readonly string FragmentShader = BuildShader();

    private static string BuildShader()
    {
        static string F(double v) => v.ToString("0.0###", CultureInfo.InvariantCulture);

        return $$"""
            #version 330 core
            in vec2 vTexCoord;
            out vec4 fragColor;

            uniform float uLevel;          // smoothed VU level 0..1 (the needle position)
            uniform sampler2D uBackground; // the dial face (built-in or a custom image)
            uniform float uPivotYFrac;     // hub Y as a fraction of height (per chosen origin)
            uniform float uNeedleDown;     // 1 = needle hangs DOWN (top hub), 0 = points UP (bottom hub)

            const float PI = 3.14159265;
            const float FW = {{F(VuMeterGeometry.FaceWidth)}};
            const float FH = {{F(VuMeterGeometry.FaceHeight)}};
            const float PX = {{F(VuMeterGeometry.PivotXPx)}};
            const float R  = {{F(VuMeterGeometry.ArcRadiusPx)}};
            const float AMIN = {{F(VuMeterGeometry.NeedleMinDeg)}};
            const float AMAX = {{F(VuMeterGeometry.NeedleMaxDeg)}};

            float sdSeg(vec2 p, vec2 a, vec2 b) {
                vec2 pa = p - a, ba = b - a;
                float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
                return length(pa - ba * h);
            }

            void main() {
                // The compositor presents this generator's FBO vertically flipped relative to a
                // top-row-first image, so mirror Y here once: with fc the background image and the needle's
                // pixel space both read top-left origin, so the meter appears upright (hub near the TOP,
                // needle hanging DOWN) and the needle registers with the printed face at any aspect.
                vec2 fc = vec2(vTexCoord.x, 1.0 - vTexCoord.y);
                vec2 pix = vec2(fc.x * FW, fc.y * FH);

                float ang = mix(AMIN, AMAX, clamp(uLevel, 0.0, 1.0)) * PI / 180.0;
                // dirY flips with the chosen origin: +cos hangs the needle DOWN (top hub), -cos points it
                // UP (bottom hub). Left/right (sin) is the same either way, so level 0 = far left.
                float dirY = (uNeedleDown > 0.5) ? cos(ang) : -cos(ang);
                vec2 dir = vec2(sin(ang), dirY);
                vec2 pivot = vec2(PX, uPivotYFrac * FH);
                vec2 tip  = pivot + dir * (R + 12.0);
                vec2 tail = pivot - dir * 46.0;           // short counterweight past the hub

                float len = length(tip - tail);
                float along = clamp(dot(pix - tail, (tip - tail) / len) / len, 0.0, 1.0);
                float halfW = mix(4.5, 1.1, along);        // tapered: wide at the base, fine at the tip
                float d = sdSeg(pix, tail, tip);
                float needle = smoothstep(halfW, halfW - 1.6, d);

                // The dial face is the background image; the needle is drawn over it with a soft shadow
                // so it stays legible on any custom face. Opaque output - this layer IS the whole meter.
                vec3 col = texture(uBackground, fc).rgb;
                float shadow = smoothstep(halfW + 3.0, halfW + 0.5, d);
                col *= (1.0 - 0.30 * shadow);
                col = mix(col, vec3(0.04), needle);        // near-black needle
                fragColor = vec4(col, 1.0);
            }
            """;
    }

    /// <summary>
    /// Builds the descriptor for the built-in VU-meter generator. No tunable parameters — the dial face
    /// is the <paramref name="backgroundPath"/> image (the built-in <see cref="VuMeterFace"/> by default,
    /// or a custom one) sampled as <c>uBackground</c>; the generator only animates the needle from
    /// <c>uLevel</c> over it.
    /// </summary>
    public static VisualEffectDescriptor Descriptor(
        string shaderPath, string backgroundPath, VuMeterNeedleOrigin origin) => new(
        EffectId,
        Version,
        PackageId,
        shaderPath,
        new[]
        {
            // The pivot Y and needle direction are uniforms (not baked) so one shader serves both origins;
            // re-registering with a different origin live-swaps them once the composition refreshes.
            new VisualEffectParameter("pivotY", "uPivotYFrac", Min: 0.0, Max: 1.0,
                Default: VuMeterGeometry.PivotYFrac(origin)),
            new VisualEffectParameter("needleDown", "uNeedleDown", Min: 0.0, Max: 1.0,
                Default: VuMeterGeometry.NeedleDown(origin) ? 1.0 : 0.0),
        },
        Role: VisualEffectRole.Generator,
        BackgroundImagePath: backgroundPath);

    /// <summary>The built-in dial-face image for the chosen origin (rendered by <see cref="VuMeterFace"/>).</summary>
    public static string FaceImagePath(VuMeterNeedleOrigin origin = VuMeterNeedleOrigin.Bottom)
        => VuMeterFace.EnsureCreated(origin);

    /// <summary>
    /// The spec a custom face (background) image must follow for the chosen origin so the standard needle
    /// still registers with it — surfaced to the Add-ons settings page. Derived from
    /// <see cref="VuMeterGeometry"/> (single source of truth) so the published size/pivot can never drift.
    /// </summary>
    public static VuMeterFaceSpec FaceSpec(VuMeterNeedleOrigin origin = VuMeterNeedleOrigin.Bottom) => new(
        RecommendedWidth: VuMeterGeometry.FaceWidth,
        RecommendedHeight: VuMeterGeometry.FaceHeight,
        PivotXFraction: VuMeterGeometry.PivotXFrac,
        PivotYFraction: VuMeterGeometry.PivotYFrac(origin),
        PivotXPixels: (int)Math.Round(VuMeterGeometry.PivotXPx),
        PivotYPixels: (int)Math.Round(VuMeterGeometry.PivotYPx(origin)),
        ArcRadiusFraction: VuMeterGeometry.ArcRadiusFrac,
        ArcRadiusPixels: (int)Math.Round(VuMeterGeometry.ArcRadiusPx),
        NeedleMinDegrees: VuMeterGeometry.NeedleMinDeg,
        NeedleMaxDegrees: VuMeterGeometry.NeedleMaxDeg,
        Origin: origin);

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
    /// <summary>
    /// (Re-)registers the VU-meter generator with a dial-face background. <paramref name="backgroundPath"/>
    /// is the custom face image to show behind the needle; a null/blank/missing path falls back to the
    /// built-in <see cref="VuMeterFace"/>. Calling it again with a new path live-swaps the background once
    /// the composition is refreshed (the renderer rebuilds the generator from this descriptor).
    /// </summary>
    public static bool TryRegister(
        IVisualEffectRegistry registry,
        string? backgroundPath = null,
        VuMeterNeedleOrigin origin = VuMeterNeedleOrigin.Bottom,
        Action<string>? onWarning = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        try
        {
            string shaderPath = EnsureShaderCreated();
            string background = !string.IsNullOrWhiteSpace(backgroundPath) && File.Exists(backgroundPath)
                ? backgroundPath
                : FaceImagePath(origin);
            registry.ReplacePackage(PackageId, new[] { Descriptor(shaderPath, background, origin) });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            onWarning?.Invoke($"Built-in VU-meter generator unavailable ({ex.Message}); visuals run without it.");
            return false;
        }
    }
}
