using System.Collections.Concurrent;
using System.Linq;
using Liveolator.Core.Audio;
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
/// Effect chains execute as ordered GLSL framebuffer passes before each layer is composited. Video
/// and camera sources, quantized clip launching, and transitions remain deferred.
/// </summary>
public sealed class GlVisualPerformanceEngine : IVisualPerformanceEngine, IVisualPreviewSource, IDisposable
{
    /// <summary>The macro name this slice understands; bound to the shader brightness uniform.</summary>
    public const string BrightnessMacro = "brightness";

    private readonly VisualMacro _brightnessMacro;
    private readonly IBeatClock _beatClock;
    private readonly IVisualAudioLevelSource _audioLevel;
    private readonly IVisualAudioBandsSource _audioBands;
    private readonly SkiaImageLoader _imageLoader;
    private readonly ILogger<GlVisualPerformanceEngine> _logger;
    private readonly double _flashStrength;
    private readonly IVisualEffectRegistry _effectRegistry;
    private readonly IReadOnlyList<VisualMacro> _macros;
    // Optional: lets the per-frame LayeredQuadRenderer log skipped/uncompilable layers through the same
    // sink as the engine. Null in headless tests, where the renderer falls back to NullLogger.
    private readonly ILoggerFactory? _loggerFactory;

    // Macro control values are normalized 0..1 and may be written from a UI/MIDI thread while the
    // render thread reads them, so the store is concurrent and reads take an immutable snapshot.
    private readonly ConcurrentDictionary<string, double> _macroValues = new(StringComparer.Ordinal);

    private volatile bool _blackout;

    // The ordered banks addressable by the Scene Grid / Push bank tabs, with the currently-active index.
    // Bank selection mutates _activeBankIndex (pure, observable state — unit-tested off the GPU); the
    // render loop reads ActiveBank each frame, so a SelectBank() takes effect on the next composed frame
    // without touching the GL context. The list is non-empty by construction.
    private readonly IReadOnlyList<VisualBank> _banks;
    private volatile int _activeBankIndex;
    private readonly object _sceneGate = new();
    private VisualScene? _activeScene;
    private long _compositionVersion;
    private int _previewFrameCounter;

    public event EventHandler<VisualPreviewFrame>? PreviewFrameReady;

    /// <summary>Single-bank engine (the original first-slice shape). Equivalent to one-element bank list.</summary>
    public GlVisualPerformanceEngine(
        VisualBank initialBank,
        VisualMacro brightnessMacro,
        IBeatClock beatClock,
        double flashStrength = 0.6,
        SkiaImageLoader? imageLoader = null,
        ILogger<GlVisualPerformanceEngine>? logger = null,
        IVisualEffectRegistry? effectRegistry = null,
        IReadOnlyList<VisualMacro>? macros = null,
        IVisualAudioLevelSource? audioLevel = null,
        ILoggerFactory? loggerFactory = null)
        : this(new[] { initialBank ?? throw new ArgumentNullException(nameof(initialBank)) },
               brightnessMacro, beatClock, flashStrength, imageLoader, logger, effectRegistry, macros, audioLevel, loggerFactory)
    {
    }

    /// <summary>
    /// Multi-bank engine (doc 22 C3): the Scene Grid can switch the active bank at runtime via
    /// <see cref="SelectBank"/>, which drives which scenes the pads load. The banks list must be
    /// non-empty; the first bank is active initially.
    /// </summary>
    public GlVisualPerformanceEngine(
        IReadOnlyList<VisualBank> banks,
        VisualMacro brightnessMacro,
        IBeatClock beatClock,
        double flashStrength = 0.6,
        SkiaImageLoader? imageLoader = null,
        ILogger<GlVisualPerformanceEngine>? logger = null,
        IVisualEffectRegistry? effectRegistry = null,
        IReadOnlyList<VisualMacro>? macros = null,
        IVisualAudioLevelSource? audioLevel = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(banks);
        if (banks.Count == 0)
            throw new ArgumentException("At least one visual bank is required.", nameof(banks));
        if (banks.Any(b => b is null))
            throw new ArgumentException("A visual bank entry was null.", nameof(banks));

        _banks = banks.ToArray();
        _activeBankIndex = 0;
        _activeScene = _banks[0].Scene(0);
        _compositionVersion = 1;
        _brightnessMacro = brightnessMacro ?? throw new ArgumentNullException(nameof(brightnessMacro));
        _beatClock = beatClock ?? throw new ArgumentNullException(nameof(beatClock));
        _audioLevel = audioLevel ?? new SilentVisualAudioLevelSource();
        _audioBands = _audioLevel as IVisualAudioBandsSource ?? new SilentVisualAudioLevelSource();
        if (flashStrength < 0 || double.IsNaN(flashStrength))
            throw new ArgumentOutOfRangeException(nameof(flashStrength), flashStrength, "Flash strength must be >= 0.");

        _flashStrength = flashStrength;
        _imageLoader = imageLoader ?? new SkiaImageLoader();
        _logger = logger ?? loggerFactory?.CreateLogger<GlVisualPerformanceEngine>() ?? NullLogger<GlVisualPerformanceEngine>.Instance;
        _loggerFactory = loggerFactory;
        _effectRegistry = effectRegistry ?? new VisualEffectRegistry();
        _macros = (macros ?? Array.Empty<VisualMacro>())
            .Append(brightnessMacro)
            .DistinctBy(macro => macro.Name, StringComparer.Ordinal)
            .ToArray();

        foreach (VisualMacro macro in _macros)
            _macroValues[macro.Name] = NormalizeDefault(macro);
    }

    public VisualBank ActiveBank => _banks[_activeBankIndex];

    /// <summary>The number of banks addressable by the Scene Grid / Push bank tabs.</summary>
    public int BankCount => _banks.Count;

    /// <inheritdoc />
    public IReadOnlyList<string> BankNames => _banks.Select(b => b.Name).ToArray();

    /// <summary>The index of the currently-active bank (the one the pads load scenes from).</summary>
    public int ActiveBankIndex => _activeBankIndex;

    internal long CompositionVersion => Interlocked.Read(ref _compositionVersion);

    /// <summary>Resolves the uniforms for the next frame from current macro/blackout state + clock.</summary>
    public FrameUniforms CurrentFrame()
    {
        double normalized = _macroValues.TryGetValue(BrightnessMacro, out double v) ? v : NormalizeDefault(_brightnessMacro);
        return FrameUniforms.Resolve(
            _brightnessMacro,
            normalized,
            _beatClock.Current,
            _flashStrength,
            _blackout,
            _audioLevel.Current,
            _audioBands.CurrentBands);
    }

    /// <summary>
    /// The active scene's layers resolved to their composite order, blend mode, opacity, and
    /// renderability (image layers render; video/camera are deferred → non-renderable). Pure — lets
    /// the scene→layer mapping unit-test off the GPU. The renderer draws the renderable subset.
    /// </summary>
    public IReadOnlyList<ResolvedLayer> CurrentComposition()
    {
        lock (_sceneGate)
            return SceneComposition.Resolve(_activeScene);
    }

    public void SetMacro(string name, double value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Macro name is required.", nameof(name));

        double clamped = Math.Clamp(value, 0.0, 1.0);
        _macroValues[name] = clamped;

        if (!_macros.Any(macro => string.Equals(macro.Name, name, StringComparison.Ordinal)))
            _logger.LogDebug("Macro '{Macro}' set to {Value:0.###} but is not bound in this slice; ignored by the shader.", name, clamped);
    }

    public void Blackout(bool on)
    {
        _blackout = on;
        _logger.LogInformation("Blackout {State}.", on ? "engaged" : "released");
    }

    /// <summary>
    /// Switches the active bank by index (doc 22 C3). An out-of-range index is ignored with a warning
    /// (never a silent no-op — global standard #26); a valid index takes effect on the next composed
    /// frame, so the render loop needs no GL-thread coordination. Pure state — unit-tested off the GPU.
    /// </summary>
    public void SelectBank(int index)
    {
        if (index < 0 || index >= _banks.Count)
        {
            _logger.LogWarning(
                "SelectBank({Index}) ignored: only {Count} bank(s) are loaded.", index, _banks.Count);
            return;
        }

        lock (_sceneGate)
        {
            _activeBankIndex = index;
            _activeScene = _banks[index].Scene(0);
            MarkCompositionDirty();
        }
        _logger.LogInformation("Active visual bank → {Index} '{Name}'.", index, _banks[index].Name);
    }

    // --- Scene/layer mutation plus operations still deferred to later phases. ---

    public void LoadScene(VisualScene scene, Quantize when, int everyN = 1)
    {
        ArgumentNullException.ThrowIfNull(scene);
        lock (_sceneGate)
        {
            _activeScene = scene;
            MarkCompositionDirty();
        }

        foreach ((string name, double value) in scene.MacroValues)
            _macroValues[name] = Math.Clamp(value, 0.0, 1.0);
    }

    public void SetLayerSource(int layer, VisualSourceRef source, Quantize when, int everyN = 1)
    {
        ArgumentNullException.ThrowIfNull(source);
        MutateLayer(layer, current => current with { Source = source });
    }

    public void ToggleLayer(int layer)
        => MutateLayer(layer, current => current with { Opacity = current.Opacity > 0.0 ? 0.0 : 1.0 });

    public void SetLayerOpacity(int layer, double opacity)
    {
        if (double.IsNaN(opacity))
            throw new ArgumentOutOfRangeException(nameof(opacity), opacity, "Opacity must be a number.");
        double clamped = Math.Clamp(opacity, 0.0, 1.0);
        MutateLayer(layer, current => current with { Opacity = clamped });
    }

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
        long renderedVersion = 0;
        Vector2D<int> framebufferSize = new(width, height);

        void SetViewport(Vector2D<int> size)
        {
            framebufferSize = size;
            if (gl is null)
                return;
            gl.Viewport(0, 0, (uint)Math.Max(1, size.X), (uint)Math.Max(1, size.Y));
        }

        void RefreshRenderer()
        {
            if (gl is null)
                return;

            long targetVersion = CompositionVersion;
            IReadOnlyList<(ResolvedLayer Layer, RgbaImage? Image)> layers = LoadRenderableLayers();
            LayeredQuadRenderer? next = layers.Count > 0
                ? new LayeredQuadRenderer(
                    gl,
                    layers,
                    _effectRegistry,
                    _macros,
                    _loggerFactory?.CreateLogger<LayeredQuadRenderer>() ?? NullLogger<LayeredQuadRenderer>.Instance)
                : null;

            renderer?.Dispose();
            renderer = next;
            renderedVersion = targetVersion;
            _logger.LogInformation(
                "Visual composition refreshed ({Layers} renderable layer(s), version {Version}).",
                renderer?.LayerCount ?? 0, renderedVersion);
        }

        window.Load += () =>
        {
            try
            {
                gl = GL.GetApi(window);
                SetViewport(window.FramebufferSize);
                RefreshRenderer();
                _logger.LogInformation(
                    "Visual compositor window loaded ({Width}x{Height}, {Layers} layer(s)).",
                    window.FramebufferSize.X, window.FramebufferSize.Y, renderer?.LayerCount ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize the GL compositor; closing the window.");
                window.Close();
            }
        };

        window.FramebufferResize += SetViewport;

        window.Render += _ =>
        {
            if (gl is null)
                return;
            try
            {
                if (renderedVersion != CompositionVersion)
                    RefreshRenderer();

                if (renderer is not null)
                {
                    renderer.Render(
                        CurrentFrame(),
                        framebufferSize.X,
                        framebufferSize.Y,
                        new Dictionary<string, double>(_macroValues));
                }
                else
                {
                    gl.ClearColor(0f, 0f, 0f, 1f);
                    gl.Clear((uint)ClearBufferMask.ColorBufferBit);
                }

                if (++_previewFrameCounter % 6 == 0 && PreviewFrameReady is not null)
                    PublishPreview(gl, framebufferSize.X, framebufferSize.Y);
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

    /// <summary>
    /// The active scene as currently mutated (layer sources/opacity/blend), for persistence. This is the
    /// live state behind <c>SetLayerSource</c>/<c>ToggleLayer</c> — distinct from <see cref="ActiveBank"/>,
    /// whose stored scene is not mutated in place. Null until a bank with at least one scene is active.
    /// </summary>
    public VisualScene? ActiveScene
    {
        get
        {
            lock (_sceneGate)
                return _activeScene;
        }
    }

    private void MutateLayer(int layer, Func<VisualLayer, VisualLayer> mutate)
    {
        lock (_sceneGate)
        {
            if (_activeScene is null || layer < 0)
            {
                _logger.LogWarning(
                    "Layer mutation ignored: index {Layer} is outside the active scene.", layer);
                return;
            }

            var layers = _activeScene.Layers.ToList();
            while (layers.Count <= layer)
            {
                layers.Add(new VisualLayer(
                    $"Layer {layers.Count + 1}",
                    new VisualSourceRef(VisualSourceKind.Generator, "core/vu-meter"),
                    Array.Empty<EffectRef>(),
                    BlendMode.Normal,
                    0.0));
            }
            layers[layer] = mutate(layers[layer]);
            _activeScene = _activeScene with { Layers = layers };
            MarkCompositionDirty();
        }
    }

    private void MarkCompositionDirty() => Interlocked.Increment(ref _compositionVersion);

    // Resolves every renderable layer of the active scene in composite order. An image layer is decoded
    // to an RgbaImage; a generator layer (doc 26) carries a null image (its descriptor/shader is resolved
    // by the renderer from the registry). A layer whose image fails to decode is dropped with a warning
    // (doc 08 — a missing asset degrades that layer, it does not crash the show); video/camera layers are
    // non-renderable and skipped silently.
    private IReadOnlyList<(ResolvedLayer Layer, RgbaImage? Image)> LoadRenderableLayers()
    {
        var loaded = new List<(ResolvedLayer, RgbaImage?)>();
        foreach (ResolvedLayer layer in SceneComposition.RenderableLayers(ActiveScene))
        {
            if (layer.Source.Kind == VisualSourceKind.Generator)
            {
                loaded.Add((layer, null));
                continue;
            }

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

    private unsafe void PublishPreview(GL gl, int width, int height)
    {
        int previewWidth = Math.Max(1, width);
        int previewHeight = Math.Max(1, height);
        byte[] pixels = new byte[previewWidth * previewHeight * 4];

        gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        fixed (byte* destination = pixels)
        {
            gl.ReadPixels(
                0, 0,
                (uint)previewWidth, (uint)previewHeight,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                destination);
        }

        int stride = previewWidth * 4;
        byte[] row = new byte[stride];
        for (int top = 0, bottom = previewHeight - 1; top < bottom; top++, bottom--)
        {
            System.Buffer.BlockCopy(pixels, top * stride, row, 0, stride);
            System.Buffer.BlockCopy(pixels, bottom * stride, pixels, top * stride, stride);
            System.Buffer.BlockCopy(row, 0, pixels, bottom * stride, stride);
        }

        PreviewFrameReady?.Invoke(this, new VisualPreviewFrame(previewWidth, previewHeight, pixels));
    }
}
