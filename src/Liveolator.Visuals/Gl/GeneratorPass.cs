using Liveolator.Core.Visuals;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// Renders a <see cref="VisualEffectRole.Generator"/> shader into a viewport-sized texture each frame
/// (doc 26 — the basis for generative add-ons like a VU meter). The symmetric counterpart to
/// <see cref="EffectChainRenderer"/>: where an effect samples a layer's existing texture, a generator
/// draws its pixels from uniforms alone (beat + live audio level + its declared parameters) with no
/// input texture. The produced texture is then composited by <see cref="LayeredQuadRenderer"/> with the
/// layer's blend mode + opacity, exactly like an image layer.
/// </summary>
/// <remarks>
/// Owns one program + one FBO/texture sized to the live viewport; the target is reallocated only when
/// the viewport changes, not every frame. A missing/uncompilable shader degrades to
/// <see cref="IsValid"/> = false so the layer is skipped rather than crashing the show (doc 08 rule).
/// </remarks>
internal sealed class GeneratorPass : IDisposable
{
    private static readonly float[] QuadVertices =
    {
        -1f,  1f, 0f, 0f,
        -1f, -1f, 0f, 1f,
         1f, -1f, 1f, 1f,
        -1f,  1f, 0f, 0f,
         1f, -1f, 1f, 1f,
         1f,  1f, 1f, 0f,
    };

    private readonly GL _gl;
    private readonly ILogger _logger;
    private readonly VisualEffectDescriptor _descriptor;
    private readonly Dictionary<string, int> _parameterLocations = new(StringComparer.Ordinal);

    private uint _program;
    private uint _vao;
    private uint _vbo;
    private uint _texture;
    private uint _framebuffer;
    private int _width = -1;
    private int _height = -1;

    private int _uResolution = -1;
    private int _uBeatPhase = -1;
    private int _uBarPhase = -1;
    private int _uConfidence = -1;
    private int _uBeatFlash = -1;
    private int _uRms = -1;
    private int _uPeak = -1;
    private int _uLevel = -1;

    private bool _valid;
    private bool _disposed;

    public GeneratorPass(GL gl, VisualEffectDescriptor descriptor, ILogger logger)
    {
        _gl = gl;
        _descriptor = descriptor;
        _logger = logger;

        try
        {
            string fragment = File.ReadAllText(descriptor.ShaderPath);
            BuildProgram(fragment);
            BuildQuad();
            _valid = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ShaderCompilationException)
        {
            _logger.LogWarning(
                ex, "Visual generator '{Effect}' could not be loaded; the layer will be skipped.",
                descriptor.EffectId);
            _valid = false;
        }
    }

    /// <summary>True when the generator program compiled; a false generator layer is skipped.</summary>
    public bool IsValid => _valid;

    /// <summary>
    /// Renders the generator into its own viewport-sized texture and returns the handle. Reallocates the
    /// target only when <paramref name="width"/>/<paramref name="height"/> change. Binds no input texture.
    /// </summary>
    public uint Render(
        int width,
        int height,
        FrameUniforms frame,
        IReadOnlyDictionary<string, float> parameters)
    {
        if (!_valid)
            return 0;

        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width != _width || height != _height)
            AllocateTarget(width, height);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.Disable(EnableCap.Blend);
        _gl.ClearColor(0, 0, 0, 0);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);

        _gl.UseProgram(_program);
        if (_uResolution >= 0)
            _gl.Uniform2(_uResolution, (float)width, (float)height);
        Set(_uBeatPhase, frame.BeatPhase);
        Set(_uBarPhase, frame.BarPhase);
        Set(_uConfidence, frame.Confidence);
        Set(_uBeatFlash, frame.BeatFlash);
        Set(_uRms, frame.Rms);
        Set(_uPeak, frame.Peak);
        Set(_uLevel, frame.Level);

        foreach ((string uniform, float value) in parameters)
        {
            if (_parameterLocations.TryGetValue(uniform, out int location) && location >= 0)
                _gl.Uniform1(location, value);
        }

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.BindVertexArray(0);
        return _texture;
    }

    private void Set(int location, float value)
    {
        if (location >= 0)
            _gl.Uniform1(location, value);
    }

    private void BuildProgram(string fragment)
    {
        uint vertex = Compile(ShaderType.VertexShader, LayeredQuadShaderSource.Vertex);
        uint pixel = Compile(ShaderType.FragmentShader, fragment);
        uint program = _gl.CreateProgram();
        _gl.AttachShader(program, vertex);
        _gl.AttachShader(program, pixel);
        _gl.LinkProgram(program);
        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
        string log = _gl.GetProgramInfoLog(program);
        _gl.DetachShader(program, vertex);
        _gl.DetachShader(program, pixel);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(pixel);
        if (linked == 0)
        {
            _gl.DeleteProgram(program);
            throw new ShaderCompilationException($"Generator program failed to link: {log}");
        }

        _program = program;
        _uResolution = _gl.GetUniformLocation(program, "uResolution");
        _uBeatPhase = _gl.GetUniformLocation(program, "uBeatPhase");
        _uBarPhase = _gl.GetUniformLocation(program, "uBarPhase");
        _uConfidence = _gl.GetUniformLocation(program, "uConfidence");
        _uBeatFlash = _gl.GetUniformLocation(program, "uBeatFlash");
        _uRms = _gl.GetUniformLocation(program, "uRms");
        _uPeak = _gl.GetUniformLocation(program, "uPeak");
        _uLevel = _gl.GetUniformLocation(program, "uLevel");
        foreach (VisualEffectParameter parameter in _descriptor.Parameters)
            _parameterLocations[parameter.Uniform] = _gl.GetUniformLocation(program, parameter.Uniform);
    }

    private uint Compile(ShaderType type, string source)
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

    private unsafe void AllocateTarget(int width, int height)
    {
        if (_texture != 0)
            _gl.DeleteTexture(_texture);
        if (_framebuffer != 0)
            _gl.DeleteFramebuffer(_framebuffer);

        _texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _texture);
        _gl.TexImage2D(
            TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba8,
            (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

        _framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _texture, 0);
        if (_gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
            throw new InvalidOperationException("Visual generator framebuffer is incomplete.");

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        _width = width;
        _height = height;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_texture != 0) _gl.DeleteTexture(_texture);
        if (_framebuffer != 0) _gl.DeleteFramebuffer(_framebuffer);
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
        if (_program != 0) _gl.DeleteProgram(_program);
    }
}
