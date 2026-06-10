using System.Globalization;
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

    // The needle-only generator: it draws JUST the moving black needle (and its short counterweight
    // tail) on a TRANSPARENT background, so it composites over the static face image (VuMeterFace) and
    // reacts to the audio while the printed dial stays fixed. Contract per doc 26: #version 330 core,
    // in vec2 vTexCoord, out vec4 fragColor, premultiplied-alpha output; uLevel drives the needle angle.
    // It works in FACE PIXEL SPACE using the shared VuMeterGeometry, so the needle aligns with the arc
    // the face renderer printed. Built from the geometry constants so there is one source of truth.
    public static readonly string FragmentShader = BuildShader();

    private static string BuildShader()
    {
        static string F(double v) => v.ToString("0.0###", CultureInfo.InvariantCulture);

        return $$"""
            #version 330 core
            in vec2 vTexCoord;
            out vec4 fragColor;

            uniform float uLevel;   // smoothed VU level 0..1 (the needle position)

            const float PI = 3.14159265;
            const float FW = {{F(VuMeterGeometry.FaceWidth)}};
            const float FH = {{F(VuMeterGeometry.FaceHeight)}};
            const float PX = {{F(VuMeterGeometry.PivotXPx)}};
            const float PY = {{F(VuMeterGeometry.PivotYPx)}};
            const float R  = {{F(VuMeterGeometry.ArcRadiusPx)}};
            const float AMIN = {{F(VuMeterGeometry.NeedleMinDeg)}};
            const float AMAX = {{F(VuMeterGeometry.NeedleMaxDeg)}};

            float sdSeg(vec2 p, vec2 a, vec2 b) {
                vec2 pa = p - a, ba = b - a;
                float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
                return length(pa - ba * h);
            }

            void main() {
                // Face pixel space (y down), so it registers with the printed face at any window aspect.
                vec2 pix = vec2(vTexCoord.x * FW, vTexCoord.y * FH);

                float ang = mix(AMIN, AMAX, clamp(uLevel, 0.0, 1.0)) * PI / 180.0;
                vec2 dir = vec2(sin(ang), -cos(ang));     // up = -y, + = right
                vec2 pivot = vec2(PX, PY);
                vec2 tip  = pivot + dir * (R + 12.0);
                vec2 tail = pivot - dir * 46.0;           // short counterweight past the hub

                float len = length(tip - tail);
                float along = clamp(dot(pix - tail, (tip - tail) / len) / len, 0.0, 1.0);
                float halfW = mix(4.5, 1.1, along);        // tapered: wide at the base, fine at the tip
                float d = sdSeg(pix, tail, tip);
                float needle = smoothstep(halfW, halfW - 1.6, d);

                vec3 col = vec3(0.05);                     // near-black needle
                fragColor = vec4(col * needle, needle);    // premultiplied; transparent elsewhere
            }
            """;
    }

    /// <summary>
    /// Builds the descriptor for the built-in needle generator. No tunable parameters — the dial face
    /// (colours, scale, red zone) is the static <see cref="VuMeterFace"/> image; the generator only
    /// animates the needle from <c>uLevel</c>.
    /// </summary>
    public static VisualEffectDescriptor Descriptor(string shaderPath) => new(
        EffectId,
        Version,
        PackageId,
        shaderPath,
        Array.Empty<VisualEffectParameter>(),
        Role: VisualEffectRole.Generator);

    /// <summary>The static dial-face image this needle composites over (rendered by <see cref="VuMeterFace"/>).</summary>
    public static string FaceImagePath() => VuMeterFace.EnsureCreated();

    /// <summary>
    /// The spec a custom face (background) image must follow so the standard needle still registers with
    /// it — surfaced to the Add-ons settings page. Derived from <see cref="VuMeterGeometry"/> (single
    /// source of truth) so the published size/pivot can never drift from the shader and face renderer.
    /// </summary>
    public static VuMeterFaceSpec FaceSpec { get; } = new(
        RecommendedWidth: VuMeterGeometry.FaceWidth,
        RecommendedHeight: VuMeterGeometry.FaceHeight,
        PivotXFraction: VuMeterGeometry.PivotXFrac,
        PivotYFraction: VuMeterGeometry.PivotYFrac,
        PivotXPixels: (int)Math.Round(VuMeterGeometry.PivotXPx),
        PivotYPixels: (int)Math.Round(VuMeterGeometry.PivotYPx),
        ArcRadiusFraction: VuMeterGeometry.ArcRadiusFrac,
        ArcRadiusPixels: (int)Math.Round(VuMeterGeometry.ArcRadiusPx),
        NeedleMinDegrees: VuMeterGeometry.NeedleMinDeg,
        NeedleMaxDegrees: VuMeterGeometry.NeedleMaxDeg);

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
