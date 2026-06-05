namespace Liveolator.Visuals.Gl;

/// <summary>
/// GLSL source for the single-layer slice: a fullscreen-quad vertex shader and a fragment shader
/// that samples the layer texture and applies the one controllable effect — a brightness multiplier
/// driven by the brightness macro and pulsed on the beat (doc 08 "per-frame reactive parameters").
/// The effect chain grows by adding uniforms/branches here and exposing them as macros.
/// </summary>
internal static class QuadShaderSource
{
    // Two attributes: clip-space position and the matching texture coordinate. The quad covers the
    // whole viewport so there is no projection matrix in this slice.
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

    // uBrightness: base multiplier from the macro. uBeatFlash: additive pulse from the beat clock.
    // uBlackout: hard cut to black (panic). The CPU resolves all three in FrameUniforms so the
    // shader stays trivial and the reactive math is unit-tested.
    public const string Fragment = """
        #version 330 core
        in vec2 vTexCoord;
        out vec4 fragColor;
        uniform sampler2D uTexture;
        uniform float uBrightness;
        uniform float uBeatFlash;
        uniform int uBlackout;
        void main()
        {
            if (uBlackout != 0)
            {
                fragColor = vec4(0.0, 0.0, 0.0, 1.0);
                return;
            }
            vec4 texel = texture(uTexture, vTexCoord);
            float gain = max(0.0, uBrightness + uBeatFlash);
            fragColor = vec4(texel.rgb * gain, texel.a);
        }
        """;
}
