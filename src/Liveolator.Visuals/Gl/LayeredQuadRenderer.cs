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
    private bool _disposed;

    /// <param name="layers">
    /// The renderable layers in composite order (bottom→top), each paired with its decoded image. The
    /// first is the opaque base; the rest blend over it. Must contain at least one layer.
    /// </param>
    public LayeredQuadRenderer(
        GL gl,
        IReadOnlyList<(ResolvedLayer Layer, RgbaImage Image)> layers,
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
    public void Render(
        FrameUniforms uniforms,
        int viewportWidth,
        int viewportHeight,
        IReadOnlyDictionary<string, double>? macroValues = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);

        _gl.UseProgram(_program);
        _gl.Uniform1(_uBrightness, uniforms.Brightness);
        _gl.Uniform1(_uBeatFlash, uniforms.BeatFlash);
        _gl.Uniform1(_uBlackout, uniforms.Blackout ? 1 : 0);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindVertexArray(_vao);

        for (int i = 0; i < _layers.Count; i++)
        {
            LayerTexture layer = _layers[i];

            // The base layer paints over the cleared frame opaquely; layers above blend over it.
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

            IReadOnlyList<ResolvedEffectParameters> effectValues = EffectParameterResolver.Resolve(
                i,
                layer.Effects,
                _effectRegistry,
                _macros,
                macroValues ?? new Dictionary<string, double>());
            uint texture = layer.EffectChain.Apply(layer.Texture, effectValues, uniforms);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            _gl.Viewport(0, 0, (uint)Math.Max(1, viewportWidth), (uint)Math.Max(1, viewportHeight));
            _gl.UseProgram(_program);
            _gl.BindVertexArray(_vao);
            _gl.Uniform1(_uBrightness, uniforms.Brightness);
            _gl.Uniform1(_uBeatFlash, uniforms.BeatFlash);
            _gl.Uniform1(_uBlackout, uniforms.Blackout ? 1 : 0);
            _gl.Uniform1(_uOpacity, (float)layer.Opacity);
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
        _gl.ShaderSource(shader, source);
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
        IReadOnlyList<(ResolvedLayer Layer, RgbaImage Image)> layers)
    {
        var built = new List<LayerTexture>(layers.Count);
        for (int index = 0; index < layers.Count; index++)
        {
            (ResolvedLayer layer, RgbaImage image) = layers[index];
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

            uint texture = BuildTexture(image);
            IReadOnlyList<ResolvedEffectParameters> effects = EffectParameterResolver.Resolve(
                index,
                layer.Effects,
                _effectRegistry,
                _macros,
                new Dictionary<string, double>());
            var effectChain = new EffectChainRenderer(
                _gl,
                image.Width,
                image.Height,
                effects,
                _logger);
            built.Add(new LayerTexture(texture, blend, layer.Opacity, layer.Effects, effectChain));
        }

        // The base layer must exist; BuildTexture validates each image and throws on a bad buffer,
        // so a fully-empty stack here is a programming error the ctor guard already prevents.
        return built;
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

    // One uploaded layer: its texture handle, the resolved GL blend state, and its opacity uniform.
    private readonly record struct LayerTexture(
        uint Texture,
        BlendModeGl Blend,
        double Opacity,
        IReadOnlyList<EffectRef> Effects,
        EffectChainRenderer EffectChain);
}
