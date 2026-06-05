namespace Liveolator.Core.Visuals;

/// <summary>
/// What a macro drives: a named parameter on a specific layer. This indirection keeps the engine
/// decoupled from concrete shaders — adding a macro is data, not new control plumbing (doc 08).
/// </summary>
/// <param name="Layer">Index of the layer whose parameter the macro writes.</param>
/// <param name="Parameter">Parameter name, e.g. "opacity", "speed", "echo.feedback".</param>
public sealed record MacroTarget(int Layer, string Parameter);
