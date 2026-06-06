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
/// First concrete <see cref="IVisualPerformanceEngine"/> over the OpenGL compositor (doc 08). Macro
/// values, blackout, and bank selection are pure, observable state (so they unit-test off the GPU via
/// <see cref="CurrentFrame"/> / <see cref="CurrentComposition"/>); <see cref="Run"/> opens a window,
/// creates the GL context + <see cref="LayeredQuadRenderer"/>, and renders the active scene's layer
/// stack each frame against the shared <see cref="IBeatClock"/>.
///
/// Now composites the active scene's full layer stack with per-layer blend modes + opacity
/// (<see cref="LayeredQuadRenderer"/>); a single image layer reproduces the original single-layer
/// behaviour. The beat clock is the shared live clock supplied at construction, so visuals react to
/// the same music the DJ side drives (the RENDER-WINDOW SEAM clock half — doc 18).
///
/// Still deferred (structured to grow into this class, not replace it): video and camera layer
/// sources (they resolve as non-renderable and are skipped), quantized scene/clip launching via
/// <see cref="IBeatScheduler"/>, and transitions. Those operations log and no-op here rather than
/// failing the build or the render loop.
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

    /// <summary>
    /// The active scene's layers resolved to their composite order, blend mode, opacity, and
    /// renderability (image layers render; video/camera are deferred → non-renderable). Pure — lets
    /// the scene→layer mapping unit-test off the GPU. The renderer draws the renderable subset.
    /// </summary>
    public IReadOnlyList<ResolvedLayer> CurrentComposition()
        => SceneComposition.Resolve(ActiveScene());

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
    /// Opens a window, creates the GL context, loads the active scene's renderable (image) layers in
    /// composite order, and renders the blended layer stack until the window closes. Requires a
    /// display — this is the manually-verified entry point (see Visuals CLAUDE/test notes); it has no
    /// headless path.
    /// </summary>
    /// <param name="title">Window title.</param>
    /// <param name="width">Initial window width in pixels.</param>
    /// <param name="height">Initial window height in pixels.</param>
    public void Run(string title = "Liveolator Visuals", int width = 1280, int height = 720)
    {
        IReadOnlyList<(ResolvedLayer Layer, RgbaImage Image)> layers = LoadRenderableLayers();
        if (layers.Count == 0)
            throw new InvalidOperationException("The active scene has no renderable image layer to render.");

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
        LayeredQuadRenderer? renderer = null;

        window.Load += () =>
        {
            try
            {
                gl = GL.GetApi(window);
                renderer = new LayeredQuadRenderer(gl, layers, NullLogger<LayeredQuadRenderer>.Instance);
                _logger.LogInformation(
                    "Visual compositor window loaded ({Width}x{Height}, {Layers} layer(s)).",
                    width, height, renderer.LayerCount);
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

    // The active scene; richer scene/bank selection arrives with LoadScene + the VisualActionHandler.
    private VisualScene? ActiveScene()
        => _activeBank.Scenes.Count > 0 ? _activeBank.Scenes[0] : null;

    // Decodes every renderable (image) layer of the active scene in composite order. A layer whose
    // image fails to decode is dropped with a warning (doc 08 — a missing asset degrades that layer,
    // it does not crash the show); video/camera layers are non-renderable and skipped silently.
    private IReadOnlyList<(ResolvedLayer Layer, RgbaImage Image)> LoadRenderableLayers()
    {
        var loaded = new List<(ResolvedLayer, RgbaImage)>();
        foreach (ResolvedLayer layer in SceneComposition.RenderableLayers(ActiveScene()))
        {
            try
            {
                RgbaImage image = _imageLoader.Load(layer.Source.Reference);
                loaded.Add((layer, image));
            }
            catch (ImageLoadException ex)
            {
                _logger.LogWarning(
                    ex, "Layer '{Layer}' image '{Reference}' could not be loaded; skipping that layer.",
                    layer.Name, layer.Source.Reference);
            }
        }
        return loaded;
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
