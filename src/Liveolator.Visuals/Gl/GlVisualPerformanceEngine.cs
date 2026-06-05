using System.Collections.Concurrent;
using Liveolator.Core.Beat;
using Liveolator.Core.Visuals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// First concrete <see cref="IVisualPerformanceEngine"/> over the OpenGL compositor — the single
/// vertical slice of doc 08: one image-backed fullscreen layer with one beat-reactive brightness
/// effect. Macro values, blackout, and bank selection are pure, observable state (so they unit-test
/// off the GPU via <see cref="CurrentFrame"/>); <see cref="Run"/> opens a window, creates the GL
/// context + <see cref="QuadRenderer"/>, and renders <see cref="CurrentFrame"/> each frame against
/// the shared <see cref="IBeatClock"/>.
///
/// Deferred to later phases (structured to grow into this class, not replace it): the full
/// layer/effect chain + blend modes, video and camera sources, quantized scene/clip launching via
/// <see cref="IBeatScheduler"/>, transitions, and the <c>VisualActionHandler</c> dispatcher bridge.
/// Those operations log and no-op here rather than failing the build or the render loop.
/// </summary>
public sealed class GlVisualPerformanceEngine : IVisualPerformanceEngine, IDisposable
{
    /// <summary>The macro name this slice understands; bound to the shader brightness uniform.</summary>
    public const string BrightnessMacro = "brightness";

    private readonly VisualMacro _brightnessMacro;
    private readonly IBeatClock _beatClock;
    private readonly SkiaImageLoader _imageLoader;
    private readonly ILogger<GlVisualPerformanceEngine> _logger;
    private readonly double _flashStrength;

    // Macro control values are normalized 0..1 and may be written from a UI/MIDI thread while the
    // render thread reads them, so the store is concurrent and reads take an immutable snapshot.
    private readonly ConcurrentDictionary<string, double> _macroValues = new(StringComparer.Ordinal);

    private volatile bool _blackout;
    private readonly VisualBank _activeBank;

    public GlVisualPerformanceEngine(
        VisualBank initialBank,
        VisualMacro brightnessMacro,
        IBeatClock beatClock,
        double flashStrength = 0.6,
        SkiaImageLoader? imageLoader = null,
        ILogger<GlVisualPerformanceEngine>? logger = null)
    {
        _activeBank = initialBank ?? throw new ArgumentNullException(nameof(initialBank));
        _brightnessMacro = brightnessMacro ?? throw new ArgumentNullException(nameof(brightnessMacro));
        _beatClock = beatClock ?? throw new ArgumentNullException(nameof(beatClock));
        if (flashStrength < 0 || double.IsNaN(flashStrength))
            throw new ArgumentOutOfRangeException(nameof(flashStrength), flashStrength, "Flash strength must be >= 0.");

        _flashStrength = flashStrength;
        _imageLoader = imageLoader ?? new SkiaImageLoader();
        _logger = logger ?? NullLogger<GlVisualPerformanceEngine>.Instance;

        // Seed the brightness macro with its default so the first frame is well-defined.
        _macroValues[BrightnessMacro] = NormalizeDefault(brightnessMacro);
    }

    public VisualBank ActiveBank => _activeBank;

    /// <summary>Resolves the uniforms for the next frame from current macro/blackout state + clock.</summary>
    public FrameUniforms CurrentFrame()
    {
        double normalized = _macroValues.TryGetValue(BrightnessMacro, out double v) ? v : NormalizeDefault(_brightnessMacro);
        return FrameUniforms.Resolve(_brightnessMacro, normalized, _beatClock.Current, _flashStrength, _blackout);
    }

    public void SetMacro(string name, double value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Macro name is required.", nameof(name));

        double clamped = Math.Clamp(value, 0.0, 1.0);
        _macroValues[name] = clamped;

        if (!string.Equals(name, BrightnessMacro, StringComparison.Ordinal))
            _logger.LogDebug("Macro '{Macro}' set to {Value:0.###} but is not bound in this slice; ignored by the shader.", name, clamped);
    }

    public void Blackout(bool on)
    {
        _blackout = on;
        _logger.LogInformation("Blackout {State}.", on ? "engaged" : "released");
    }

    public void SelectBank(int index)
        => _logger.LogDebug("SelectBank({Index}) is deferred; the slice ships a single bank.", index);

    // --- Deferred operations (later phases): logged no-ops so callers/tests are honest about scope. ---

    public void LoadScene(VisualScene scene, Quantize when, int everyN = 1)
        => LogDeferred(nameof(LoadScene));

    public void SetLayerSource(int layer, VisualSourceRef source, Quantize when, int everyN = 1)
        => LogDeferred(nameof(SetLayerSource));

    public void ToggleLayer(int layer) => LogDeferred(nameof(ToggleLayer));

    public void SetLayerOpacity(int layer, double opacity) => LogDeferred(nameof(SetLayerOpacity));

    public void LaunchClip(int layer, string clipId, Quantize when, int everyN = 1)
        => LogDeferred(nameof(LaunchClip));

    public void Strobe(bool on) => LogDeferred(nameof(Strobe));

    public void Transition(TransitionStyle style, Quantize when, int everyN = 1)
        => LogDeferred(nameof(Transition));

    /// <summary>
    /// Opens a window, creates the GL context, loads the first image layer of
    /// <see cref="ActiveBank"/>, and renders until the window closes. Requires a display — this is
    /// the manually-verified entry point (see Visuals CLAUDE/test notes); it has no headless path.
    /// </summary>
    /// <param name="title">Window title.</param>
    /// <param name="width">Initial window width in pixels.</param>
    /// <param name="height">Initial window height in pixels.</param>
    public void Run(string title = "Liveolator Visuals", int width = 1280, int height = 720)
    {
        VisualSourceRef? source = FirstImageSource();
        if (source is null)
            throw new InvalidOperationException("The active bank has no image layer to render in this slice.");

        RgbaImage image = _imageLoader.Load(source.Reference);

        var options = WindowOptions.Default with
        {
            Title = title,
            Size = new Vector2D<int>(width, height),
            API = new GraphicsAPI(
                ContextAPI.OpenGL,
                ContextProfile.Core,
                ContextFlags.ForwardCompatible,
                new APIVersion(3, 3)),
        };

        using IWindow window = Window.Create(options);
        GL? gl = null;
        QuadRenderer? renderer = null;

        window.Load += () =>
        {
            try
            {
                gl = GL.GetApi(window);
                renderer = new QuadRenderer(gl, image, NullLogger<QuadRenderer>.Instance);
                _logger.LogInformation("Visual compositor window loaded ({Width}x{Height}).", width, height);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize the GL compositor; closing the window.");
                window.Close();
            }
        };

        window.Render += _ =>
        {
            if (gl is null || renderer is null)
                return;
            try
            {
                renderer.Render(CurrentFrame());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GL render frame failed; closing the window.");
                window.Close();
            }
        };

        window.Closing += () =>
        {
            renderer?.Dispose();
            renderer = null;
        };

        window.Run();
    }

    public void Dispose()
    {
        // The window/renderer are owned for the lifetime of Run(); nothing persists after it returns.
    }

    private VisualSourceRef? FirstImageSource()
    {
        // The slice renders the first scene's first image layer; richer scene/bank resolution
        // arrives with LoadScene + the VisualActionHandler.
        VisualScene? scene = _activeBank.Scenes.Count > 0 ? _activeBank.Scenes[0] : null;
        if (scene is null)
            return null;

        foreach (VisualLayer layer in scene.Layers)
        {
            if (layer.Source.Kind == VisualSourceKind.Image)
                return layer.Source;
        }
        return null;
    }

    private void LogDeferred(string operation)
        => _logger.LogDebug("{Operation} is deferred to a later compositor phase; no-op in this slice.", operation);

    private static double NormalizeDefault(VisualMacro macro)
    {
        // Map the macro's real Default back to a 0..1 control value so seeding matches Resolve's domain.
        double range = macro.Max - macro.Min;
        return range <= 0 ? 0.0 : Math.Clamp((macro.Default - macro.Min) / range, 0.0, 1.0);
    }
}
