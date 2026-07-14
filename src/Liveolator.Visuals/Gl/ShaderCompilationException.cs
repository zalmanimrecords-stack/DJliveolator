namespace Liveolator.Visuals.Gl;

/// <summary>
/// Raised when a GLSL shader fails to compile or a program fails to link. The compositor surfaces
/// the GL info-log so a bad shader is diagnosable; a real effect chain would fall back to a
/// pass-through effect rather than abort (doc 08 error handling).
/// </summary>
public sealed class ShaderCompilationException : Exception
{
    public ShaderCompilationException(string message) : base(message) { }

    public ShaderCompilationException(string message, Exception innerException) : base(message, innerException) { }
}
