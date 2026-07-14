using Liveolator.Core.Visuals;
using Silk.NET.OpenGL;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// Pure mapping from the platform-agnostic <see cref="BlendMode"/> (doc 08) to the GL fixed-function
/// blend state a layer is drawn with — a blend equation plus source/destination factors. Keeping it
/// GL-free-but-typed (the factor/equation enums are data) lets the layer→blend resolution unit-test
/// off the GPU; <see cref="LayeredQuadRenderer"/> just feeds these into <c>glBlendEquation</c> /
/// <c>glBlendFunc</c> before each layer's draw.
///
/// The incoming fragment is PREMULTIPLIED-alpha (rgb already scaled by opacity·texelAlpha in the
/// fragment shader), so the factors here account for opacity without a separate constant-alpha.
/// Composited bottom→top.
/// </summary>
/// <param name="SourceFactor">The source (incoming layer) blend factor.</param>
/// <param name="DestFactor">The destination (already-composited) blend factor.</param>
public readonly record struct BlendModeGl(BlendingFactor SourceFactor, BlendingFactor DestFactor)
{
    /// <summary>The blend equation; all supported modes are additive combinations via the factors.</summary>
    public BlendEquationModeEXT Equation => BlendEquationModeEXT.FuncAdd;

    /// <summary>
    /// Resolves the GL blend state for a layer's <see cref="BlendMode"/> using only separable,
    /// fixed-function blending over a PREMULTIPLIED-alpha source (portable, no framebuffer read-back):
    /// <list type="bullet">
    /// <item><see cref="BlendMode.Normal"/> — alpha-over: src + dst·(1−srcA).</item>
    /// <item><see cref="BlendMode.Add"/> — additive: src + dst (glows / light).</item>
    /// <item><see cref="BlendMode.Screen"/> — src + dst·(1−src): inverse-multiply lightening.</item>
    /// <item><see cref="BlendMode.Multiply"/> — opacity-lerped darken: src·dst + dst·(1−srcA) =
    /// dst·(src + 1 − a), so opacity 0 leaves the destination unchanged and opacity 1 gives src·dst.</item>
    /// </list>
    /// <see cref="BlendMode.Overlay"/> is non-separable (needs the destination in-shader) and is not
    /// expressible in fixed-function blending; <see cref="TryResolve"/> reports that so the renderer
    /// degrades it to <see cref="BlendMode.Normal"/> with a warning rather than rendering it wrong.
    /// </summary>
    public static BlendModeGl Resolve(BlendMode mode)
        => TryResolve(mode, out BlendModeGl gl)
            ? gl
            : throw new ArgumentOutOfRangeException(nameof(mode), mode, "Blend mode has no fixed-function GL mapping.");

    /// <summary>
    /// Resolves <paramref name="mode"/> to fixed-function blend state, returning false for modes that
    /// cannot be expressed without a framebuffer read-back (currently only <see cref="BlendMode.Overlay"/>).
    /// </summary>
    public static bool TryResolve(BlendMode mode, out BlendModeGl gl)
    {
        switch (mode)
        {
            case BlendMode.Normal:
                gl = new(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                return true;
            case BlendMode.Add:
                gl = new(BlendingFactor.One, BlendingFactor.One);
                return true;
            case BlendMode.Screen:
                gl = new(BlendingFactor.One, BlendingFactor.OneMinusSrcColor);
                return true;
            case BlendMode.Multiply:
                gl = new(BlendingFactor.DstColor, BlendingFactor.OneMinusSrcAlpha);
                return true;
            default:
                gl = default;
                return false;
        }
    }
}
