using Liveolator.Core.Visuals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.OpenGL;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// The multi-layer GPU compositor (doc 08 — a scene is a real layer stack). Grows from
/// <see cref="QuadRenderer"/>: one shared fullscreen-quad program + VAO, one texture per layer, drawn
/// bottom→top with each layer's <see cref="BlendModeGl"/> and per-layer opacity. With a single image
/// layer it reproduces the original single-layer slice (base layer drawn opaque, no blending).
///
/// Owns its GL objects and disposes them. Requires a current GL context — created and driven only
/// from the render thread (doc 08). A layer whose texture failed to build is skipped (logged) so one
/// bad asset degrades that layer instead of crashing the show (doc 08 error handling). Overlay blend
/// is non-separable and degrades to <see cref="BlendMode.Normal"/> with a warning.
/// </summary>
public sealed class LayeredQuadRenderer : IDisposable
{
    // Fullscreen quad as two triangles: clip-space xy + texture uv. V is flipped (1 - y) because the
    // image is decoded top-row-first while GL texture space is bottom-up. Matches QuadRenderer.
    private static readonly float[] QuadVertices =
    {
        // x      y      u     v
        -1f,  1f,   0f, 0f,
        -1f, -1f,   0f, 1f,
         1f, -1f,   1f, 1f,

        -1f,  1f,   0f, 0f,
         1f, -1f,   1f, 1f,
         1f,  1f,   1f, 0f,
    };

    // Fallback render size for a generator's optional post-effect chain (the built-in VU generator has
    // none). Real generator output tracks the live viewport via GeneratorPass.
    private const int DefaultEffectChainWidth = 1280;
    private const int DefaultEffectChainHeight = 720;

    private static readonly IReadOnlyDictionary<string, double> EmptyMacroValues =
        new Dictionary<string, double>();
    private static readonly IReadOnlyDictionary<string, float> EmptyGeneratorParameters =
        new Dictionary<string, float>();

    private readonly GL _gl;
    private readonly ILogger<LayeredQuadRenderer> _logger;
    private readonly IReadOnlyList<LayerTexture> _layers;
    private readonly IVisualEffectRegistry _effectRegistry;
    private readonly IReadOnlyList<VisualMacro> _macros;

    private uint _program;
    private uint _vao;
    private uint _vbo;

    private int _uBrightness;
    private int _uBeatFlash;
    private int _uBlackout;
    private int _uOpacity;
    private int _uStrobe;
    private bool _disposed;

    /// <param name="layers">
    /// The renderable layers in composite order (bottom→top). An <b>image</b> layer carries its decoded
    /// image; a <b>generator</b> layer (doc 26) carries a null image and is drawn each frame from its
    /// shader. The first is the opaque base; the rest blend over it. Must contain at least one layer.
    /// </param>
    public LayeredQuadRenderer(
        GL gl,
        IReadOnlyList<(ResolvedLayer Layer, RgbaImage? Image)> layers,
        IVisualEffectRegistry? effectRegistry = null,
        IReadOnlyList<VisualMacro>? macros = null,
        ILogger<LayeredQuadRenderer>? logger = null)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0)
            throw new ArgumentException("At least one layer is required to render.", nameof(layers));
        _logger = logger ?? NullLogger<LayeredQuadRenderer>.Instance;
        _effectRegistry = effectRegistry ?? new VisualEffectRegistry();
        _macros = macros ?? Array.Empty<VisualMacro>();

        _program = BuildProgram();
        CacheUniformLocations();
        BuildQuad();
        _layers = BuildLayerTextures(layers);
    }

    /// <summary>The number of layers whose textures were built and will be drawn.</summary>
    public int LayerCount => _layers.Count;

    /// <summary>Clears and draws the layer stack bottom→top for one frame using the resolved uniforms.</summary>
    /// <param name="liveOpacities">
    /// Optional per-layer opacities (in this renderer's composite order) read live each frame, so a
    /// <c>SetLayerOpacity</c>/<c>ToggleLayer</c> takes effect without rebuilding the renderer (doc 27 B5).
    /// Applied only when its count matches the built layer count — i.e. no layer was dropped for a failed
    /// asset, so positions align; otherwise the opacity baked at build time is used.
    /// </param>
    public void Render(
        FrameUniforms uniforms,
        int viewportWidth,
        int viewportHeight,
        IReadOnlyDictionary<string, double>? macroValues = null,
        IReadOnlyList<double>? liveOpacities = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool useLiveOpacities = liveOpacities is not null && liveOpacities.Count == _layers.Count;

        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);

        _gl.UseProgram(_program);
        _gl.Uniform1(_uBrightness, uniforms.Brightness);
        _gl.Uniform1(_uBeatFlash, uniforms.BeatFlash);
        _gl.Uniform1(_uBlackout, uniforms.Blackout ? 1 : 0);
        _gl.Uniform1(_uStrobe, uniforms.Strobe);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindVertexArray(_vao);

        for (int i = 0; i < _layers.Count; i++)
        {
            LayerTexture layer = _layers[i];

            IReadOnlyDictionary<string, double> resolvedMacros = macroValues ?? EmptyMacroValues;

            // A generator layer draws its texture from its shader each frame (the needle moves); an image
            // layer reuses its uploaded static texture. Either becomes the input to the effect chain.
            uint source;
            if (layer.Generator is { IsValid: true } generator)
            {
                // Resolve by the layer's SCENE slot, not the draw index i: a skipped "None" layer beneath
                // compacts the draw list, so i != slot, and macros (which target the slot) would miss.
                IReadOnlyDictionary<string, float> generatorParameters = ResolveGeneratorParameters(
                    layer.Slot, layer.GeneratorRef, resolvedMacros);
                source = generator.Render(
                    Math.Max(1, viewportWidth), Math.Max(1, viewportHeight), uniforms, generatorParameters);
            }
            else
            {
                source = layer.Texture;
            }

            IReadOnlyList<ResolvedEffectParameters> effectValues = EffectParameterResolver.Resolve(
                layer.Slot,
                layer.Effects,
                _effectRegistry,
                _macros,
                resolvedMacros);
            uint texture = layer.EffectChain.Apply(source, effectValues, uniforms);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            _gl.Viewport(0, 0, (uint)Math.Max(1, viewportWidth), (uint)Math.Max(1, viewportHeight));

            // Blend state is applied HERE, after the generator/effect FBO passes above — those bind their
            // own framebuffers and (GeneratorPass) disable blending, so configuring it before them would
            // leak: the composite draw would run with blending off and an opacity-0 layer would overwrite
            // the frame with transparent black. The base layer paints opaquely; layers above blend over it.
            if (i == 0)
            {
                _gl.Disable(EnableCap.Blend);
            }
            else
            {
                _gl.Enable(EnableCap.Blend);
                _gl.BlendEquation(layer.Blend.Equation);
                _gl.BlendFunc(layer.Blend.SourceFactor, layer.Blend.DestFactor);
            }

            _gl.UseProgram(_program);
            _gl.BindVertexArray(_vao);
            _gl.Uniform1(_uBrightness, uniforms.Brightness);
            _gl.Uniform1(_uBeatFlash, uniforms.BeatFlash);
            _gl.Uniform1(_uBlackout, uniforms.Blackout ? 1 : 0);
            _gl.Uniform1(_uStrobe, uniforms.Strobe);
            double opacity = useLiveOpacities ? liveOpacities![i] : layer.Opacity;
            _gl.Uniform1(_uOpacity, (float)opacity);
            _gl.BindTexture(TextureTarget.Texture2D, texture);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        }

        _gl.Disable(EnableCap.Blend);
        _gl.BindVertexArray(0);
    }

    private uint BuildProgram()
    {
        uint vertex = CompileShader(ShaderType.VertexShader, LayeredQuadShaderSource.Vertex);
        uint fragment = CompileShader(ShaderType.FragmentShader, LayeredQuadShaderSource.Fragment);

        uint program = _gl.CreateProgram();
        _gl.AttachShader(program, vertex);
        _gl.AttachShader(program, fragment);
        _gl.LinkProgram(program);

        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
        if (linked == 0)
        {
            string log = _gl.GetProgramInfoLog(program);
            _gl.DeleteProgram(program);
            _gl.DeleteShader(vertex);
            _gl.DeleteShader(fragment);
            throw new ShaderCompilationException($"Layered quad program failed to link: {log}");
        }

        _gl.DetachShader(program, vertex);
        _gl.DetachShader(program, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
        return program;
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, ShaderText.Sanitize(source));
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            string log = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new ShaderCompilationException($"{type} failed to compile: {log}");
        }
        return shader;
    }

    private void CacheUniformLocations()
    {
        _uBrightness = _gl.GetUniformLocation(_program, "uBrightness");
        _uBeatFlash = _gl.GetUniformLocation(_program, "uBeatFlash");
        _uBlackout = _gl.GetUniformLocation(_program, "uBlackout");
        _uOpacity = _gl.GetUniformLocation(_program, "uOpacity");
        _uStrobe = _gl.GetUniformLocation(_program, "uStrobe");

        // The sampler is fixed to unit 0; every layer binds its texture to that unit before its draw.
        _gl.UseProgram(_program);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "uTexture"), 0);
    }

    private unsafe void BuildQuad()
    {
        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);

        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* data = QuadVertices)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(QuadVertices.Length * sizeof(float)),
                data,
                BufferUsageARB.StaticDraw);
        }

        const uint stride = 4 * sizeof(float);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(2 * sizeof(float)));

        _gl.BindVertexArray(0);
    }

    private IReadOnlyList<LayerTexture> BuildLayerTextures(
        IReadOnlyList<(ResolvedLayer Layer, RgbaImage? Image)> layers)
    {
        var built = new List<LayerTexture>(layers.Count);
        for (int index = 0; index < layers.Count; index++)
        {
            (ResolvedLayer layer, RgbaImage? image) = layers[index];
            BlendMode blendMode = layer.Blend;
            if (!BlendModeGl.TryResolve(blendMode, out BlendModeGl blend))
            {
                // Overlay is non-separable; rendering it with a wrong factor set would be worse than
                // degrading. Fall back to Normal and tell the operator (doc 08 — never crash the show).
                _logger.LogWarning(
                    "Layer '{Layer}' uses blend mode {Blend} which has no fixed-function GL mapping; rendering as Normal.",
                    layer.Name, blendMode);
                blend = BlendModeGl.Resolve(BlendMode.Normal);
            }

            uint texture = 0;
            GeneratorPass? generator = null;
            EffectRef? generatorRef = null;
            int effectChainWidth;
            int effectChainHeight;

            if (image is not null)
            {
                texture = BuildTexture(image);
                effectChainWidth = image.Width;
                effectChainHeight = image.Height;
            }
            else
            {
                // Generator layer (doc 26): resolve its descriptor from the registry and build the pass.
                if (!_effectRegistry.TryGet(layer.Source.Reference, version: null, out VisualEffectDescriptor descriptor)
                    || descriptor.Role != VisualEffectRole.Generator)
                {
                    _logger.LogWarning(
                        "Layer '{Layer}' references generator '{Generator}' which is not a registered generator effect; skipping.",
                        layer.Name, layer.Source.Reference);
                    continue;
                }

                var pass = new GeneratorPass(_gl, descriptor, _logger);
                if (!pass.IsValid)
                {
                    pass.Dispose();
                    continue;
                }
                generator = pass;
                // A stable instance id (the effect id) so a macro can target this generator's parameters.
                generatorRef = new EffectRef(descriptor.EffectId, descriptor.Version, descriptor.EffectId,
                    new Dictionary<string, double>());

                // Generator post-effect chains use the initial render size; the VU-meter reference add-on
                // has no chain (the common case), so this only matters for a generator that also declares
                // effects — a documented limitation, not used by the built-in generator.
                effectChainWidth = DefaultEffectChainWidth;
                effectChainHeight = DefaultEffectChainHeight;
            }

            IReadOnlyList<ResolvedEffectParameters> effects = EffectParameterResolver.Resolve(
                layer.Slot,
                layer.Effects,
                _effectRegistry,
                _macros,
                EmptyMacroValues);
            var effectChain = new EffectChainRenderer(
                _gl,
                effectChainWidth,
                effectChainHeight,
                effects,
                _logger);
            built.Add(new LayerTexture(
                texture, generator, generatorRef, blend, layer.Opacity, layer.Effects, effectChain, layer.Slot));
        }

        return built;
    }

    // Resolves a generator layer's declared parameters (descriptor defaults + any macro overrides) into
    // shader uniforms, reusing the same machinery effects use. Returns an empty map when the generator
    // declares no parameters or has no synthetic reference.
    private IReadOnlyDictionary<string, float> ResolveGeneratorParameters(
        int layerIndex, EffectRef? generatorRef, IReadOnlyDictionary<string, double> macroValues)
    {
        if (generatorRef is null)
            return EmptyGeneratorParameters;

        IReadOnlyList<ResolvedEffectParameters> resolved = EffectParameterResolver.Resolve(
            layerIndex, new[] { generatorRef }, _effectRegistry, _macros, macroValues);
        return resolved.Count > 0 ? resolved[0].Uniforms : EmptyGeneratorParameters;
    }

    private unsafe uint BuildTexture(RgbaImage image)
    {
        image.Validated();
        uint texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);

        fixed (byte* pixels = image.Pixels)
        {
            _gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                (int)InternalFormat.Rgba,
                (uint)image.Width,
                (uint)image.Height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                pixels);
        }

        _gl.BindTexture(TextureTarget.Texture2D, 0);
        return texture;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            foreach (LayerTexture layer in _layers)
            {
                layer.EffectChain.Dispose();
                layer.Generator?.Dispose();
                if (layer.Texture != 0)
                    _gl.DeleteTexture(layer.Texture);
            }
            if (_vbo != 0) _gl.DeleteBuffer(_vbo);
            if (_vao != 0) _gl.DeleteVertexArray(_vao);
            if (_program != 0) _gl.DeleteProgram(_program);
        }
        catch (Exception ex)
        {
            // GL teardown failures must not mask the original error path; log and move on.
            _logger.LogWarning(ex, "Failed to release GL resources for the layered quad renderer.");
        }
    }

    // One resolved layer. An image layer carries a static Texture and a null Generator; a generator layer
    // carries a GeneratorPass (Texture is 0) plus a synthetic GeneratorRef for per-frame parameter
    // resolution. Both share the blend state, opacity, and optional post-effect chain.
    private readonly record struct LayerTexture(
        uint Texture,
        GeneratorPass? Generator,
        EffectRef? GeneratorRef,
        BlendModeGl Blend,
        double Opacity,
        IReadOnlyList<EffectRef> Effects,
        EffectChainRenderer EffectChain,
        int Slot);
}
