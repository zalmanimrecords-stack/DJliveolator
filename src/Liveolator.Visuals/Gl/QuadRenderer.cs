using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.OpenGL;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// The minimal GPU primitive of the compositor slice: a single fullscreen textured quad drawn
/// through <see cref="QuadShaderSource"/> with one beat-reactive brightness effect. Owns its GL
/// objects (program, VAO/VBO, texture) and disposes them. Requires a current GL context — it is
/// created and driven only from the render thread (doc 08). Multi-layer/blend-chain, video and
/// camera sources are deferred; they grow by adding more renderers/textures above this one.
/// </summary>
public sealed class QuadRenderer : IDisposable
{
    // Fullscreen quad as two triangles: clip-space xy + texture uv. V is flipped (1 - y) because the
    // image is decoded top-row-first while GL texture space is bottom-up.
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
    private readonly ILogger<QuadRenderer> _logger;

    private uint _program;
    private uint _vao;
    private uint _vbo;
    private uint _texture;

    private int _uBrightness;
    private int _uBeatFlash;
    private int _uBlackout;
    private bool _disposed;

    public QuadRenderer(GL gl, RgbaImage image, ILogger<QuadRenderer>? logger = null)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        ArgumentNullException.ThrowIfNull(image);
        _logger = logger ?? NullLogger<QuadRenderer>.Instance;

        image.Validated();
        _program = BuildProgram();
        CacheUniformLocations();
        BuildQuad();
        _texture = BuildTexture(image);
    }

    /// <summary>Clears and draws the quad for one frame using the resolved uniforms.</summary>
    public void Render(FrameUniforms uniforms)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);

        _gl.UseProgram(_program);
        _gl.Uniform1(_uBrightness, uniforms.Brightness);
        _gl.Uniform1(_uBeatFlash, uniforms.BeatFlash);
        _gl.Uniform1(_uBlackout, uniforms.Blackout ? 1 : 0);

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _texture);

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        _gl.BindVertexArray(0);
    }

    private uint BuildProgram()
    {
        uint vertex = CompileShader(ShaderType.VertexShader, QuadShaderSource.Vertex);
        uint fragment = CompileShader(ShaderType.FragmentShader, QuadShaderSource.Fragment);

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
            throw new ShaderCompilationException($"Quad program failed to link: {log}");
        }

        // Shaders are linked into the program; the standalone objects are no longer needed.
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

        // The texture sampler is fixed to unit 0 for the single-layer slice.
        _gl.UseProgram(_program);
        int uTexture = _gl.GetUniformLocation(_program, "uTexture");
        _gl.Uniform1(uTexture, 0);
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

    private unsafe uint BuildTexture(RgbaImage image)
    {
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
            if (_texture != 0) _gl.DeleteTexture(_texture);
            if (_vbo != 0) _gl.DeleteBuffer(_vbo);
            if (_vao != 0) _gl.DeleteVertexArray(_vao);
            if (_program != 0) _gl.DeleteProgram(_program);
        }
        catch (Exception ex)
        {
            // GL teardown failures must not mask the original error path; log and move on.
            _logger.LogWarning(ex, "Failed to release GL resources for the quad renderer.");
        }
    }
}
