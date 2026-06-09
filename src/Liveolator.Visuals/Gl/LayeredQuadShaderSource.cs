namespace Liveolator.Visuals.Gl;

/// <summary>
/// GLSL for the multi-layer compositor (doc 08 — a scene is a real layer stack). The fragment shader
/// adds a per-layer <c>uOpacity</c> and emits premultiplied alpha: the fragment's color/alpha is
/// scaled by opacity so the fixed-function blend (see <see cref="BlendModeGl"/>) composites each
/// layer over those beneath it at the right strength. Brightness/beat-flash/blackout still come
/// from <see cref="FrameUniforms"/> and apply per layer (the slice drives one global brightness macro
/// across the stack; per-layer macro targeting grows from here).
///
/// Drawn bottom→top: the base layer with blending off (opaque) and each layer above with its blend
/// mode's GL state. The shader stays trivial; all reactive math is resolved on the CPU and tested.
/// </summary>
internal static class LayeredQuadShaderSource
{
    // Two attributes: clip-space position and the matching texture coordinate. The quad covers the
    // whole viewport so there is no projection matrix; every layer reuses this one vertex shader.
    public const string Vertex = """
        #version 330 core
        layout (location = 0) in vec2 aPosition;
        layout (location = 1) in vec2 aTexCoord;
        out vec2 vTexCoord;
        void main()
        {
            vTexCoord = aTexCoord;
            gl_Position = vec4(aPosition, 0.0, 1.0);
        }
        """;

    public const string Fragment = """
        #version 330 core
        in vec2 vTexCoord;
        out vec4 fragColor;
        uniform sampler2D uTexture;
        uniform float uBrightness;
        uniform float uBeatFlash;
        uniform int uBlackout;
        uniform float uOpacity;
        void main()
        {
            if (uBlackout != 0)
            {
                fragColor = vec4(0.0, 0.0, 0.0, 1.0);
                return;
            }
            vec4 texel = texture(uTexture, vTexCoord);
            float gain = max(0.0, uBrightness + uBeatFlash);
            float a = clamp(uOpacity, 0.0, 1.0) * texel.a;
            // Output PREMULTIPLIED alpha (rgb already scaled by alpha). All blend modes in
            // BlendModeGl use premultiplied-alpha factors, so opacity is honored uniformly by
            // alpha-over, additive, and screen via the one src color - the shader bakes it in once.
            fragColor = vec4(texel.rgb * gain * a, a);
        }
        """;
}
