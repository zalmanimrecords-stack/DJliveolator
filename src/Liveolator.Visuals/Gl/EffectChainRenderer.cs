using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;

namespace Liveolator.Visuals.Gl;

internal sealed class EffectChainRenderer : IDisposable
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
    private readonly IReadOnlyList<EffectProgram> _programs;
    private readonly uint[] _textures = new uint[2];
    private readonly uint[] _framebuffers = new uint[2];
    private readonly int _width;
    private readonly int _height;
    private uint _vao;
    private uint _vbo;
    private bool _disposed;

    public EffectChainRenderer(
        GL gl,
        int width,
        int height,
        IReadOnlyList<ResolvedEffectParameters> effects,
        ILogger logger)
    {
        _gl = gl;
        _width = width;
        _height = height;
        _logger = logger;
        BuildQuad();

        var programs = new List<EffectProgram>(effects.Count);
        foreach (ResolvedEffectParameters effect in effects)
        {
            try
            {
                string fragment = File.ReadAllText(effect.Descriptor.ShaderPath);
                programs.Add(BuildProgram(effect, fragment));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ShaderCompilationException)
            {
                _logger.LogWarning(
                    ex,
                    "Visual effect '{Effect}' ({Instance}) could not be loaded; using pass-through.",
                    effect.Reference.EffectId,
                    effect.Reference.InstanceId);
            }
        }
        _programs = programs;

        if (_programs.Count > 0)
            BuildTargets();
    }

    public bool HasEffects => _programs.Count > 0;

    public uint Apply(
        uint sourceTexture,
        IReadOnlyList<ResolvedEffectParameters> values,
        FrameUniforms frame)
    {
        if (_programs.Count == 0)
            return sourceTexture;

        _gl.Disable(EnableCap.Blend);
        _gl.BindVertexArray(_vao);
        uint input = sourceTexture;

        for (int i = 0; i < _programs.Count; i++)
        {
            EffectProgram program = _programs[i];
            int target = i % 2;
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffers[target]);
            _gl.Viewport(0, 0, (uint)_width, (uint)_height);
            _gl.ClearColor(0, 0, 0, 0);
            _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
            _gl.UseProgram(program.Program);
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, input);
            SetCommonUniforms(program, frame);

            ResolvedEffectParameters? current = values.FirstOrDefault(
                value => string.Equals(
                    value.Reference.InstanceId,
                    program.InstanceId,
                    StringComparison.Ordinal));
            if (current is not null)
            {
                foreach ((string uniform, float value) in current.Uniforms)
                {
                    if (program.ParameterLocations.TryGetValue(uniform, out int location) && location >= 0)
                        _gl.Uniform1(location, value);
                }
            }

            _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
            input = _textures[target];
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.BindVertexArray(0);
        return input;
    }

    private void SetCommonUniforms(EffectProgram program, FrameUniforms frame)
    {
        Set(program.TextureLocation, 0);
        Set(program.BeatPhaseLocation, frame.BeatPhase);
        Set(program.BarPhaseLocation, frame.BarPhase);
        Set(program.ConfidenceLocation, frame.Confidence);
        Set(program.BeatFlashLocation, frame.BeatFlash);
    }

    private void Set(int location, int value)
    {
        if (location >= 0)
            _gl.Uniform1(location, value);
    }

    private void Set(int location, float value)
    {
        if (location >= 0)
            _gl.Uniform1(location, value);
    }

    private EffectProgram BuildProgram(ResolvedEffectParameters effect, string fragment)
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
            throw new ShaderCompilationException($"Effect program failed to link: {log}");
        }

        var parameters = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string uniform in effect.Uniforms.Keys)
            parameters[uniform] = _gl.GetUniformLocation(program, uniform);

        return new EffectProgram(
            effect.Reference.InstanceId,
            program,
            _gl.GetUniformLocation(program, "uTexture"),
            _gl.GetUniformLocation(program, "uBeatPhase"),
            _gl.GetUniformLocation(program, "uBarPhase"),
            _gl.GetUniformLocation(program, "uConfidence"),
            _gl.GetUniformLocation(program, "uBeatFlash"),
            parameters);
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

    private unsafe void BuildTargets()
    {
        for (int i = 0; i < 2; i++)
        {
            _textures[i] = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _textures[i]);
            _gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                (int)InternalFormat.Rgba8,
                (uint)_width,
                (uint)_height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                null);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

            _framebuffers[i] = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffers[i]);
            _gl.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D,
                _textures[i],
                0);
            if (_gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
                throw new InvalidOperationException("Visual effect framebuffer is incomplete.");
        }
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (EffectProgram program in _programs)
            _gl.DeleteProgram(program.Program);
        foreach (uint framebuffer in _framebuffers)
            if (framebuffer != 0) _gl.DeleteFramebuffer(framebuffer);
        foreach (uint texture in _textures)
            if (texture != 0) _gl.DeleteTexture(texture);
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
    }

    private sealed record EffectProgram(
        string InstanceId,
        uint Program,
        int TextureLocation,
        int BeatPhaseLocation,
        int BarPhaseLocation,
        int ConfidenceLocation,
        int BeatFlashLocation,
        IReadOnlyDictionary<string, int> ParameterLocations);
}
