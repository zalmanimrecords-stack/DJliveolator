using Liveolator.Visuals.Gl;

namespace Liveolator.Visuals.Tests.Gl;

public sealed class ShaderTextTests
{
    [Fact]
    public void Sanitize_converts_crlf_and_lone_cr_to_lf()
    {
        string result = ShaderText.Sanitize("a\r\nb\rc\n");

        Assert.Equal("a\nb\nc\n", result);
    }

    [Fact]
    public void Sanitize_drops_non_ascii_characters()
    {
        // An em-dash in a GLSL comment makes some drivers fail with a misleading "pre-mature EOF".
        string result = ShaderText.Sanitize("// one src color — baked in\nvoid main(){}");

        Assert.DoesNotContain('—', result);
        Assert.Equal("// one src color  baked in\nvoid main(){}", result);
    }

    [Fact]
    public void Sanitize_preserves_plain_ascii_glsl()
    {
        const string glsl = "#version 330 core\nvoid main(){ gl_FragColor = vec4(1.0); }\n";

        Assert.Equal(glsl, ShaderText.Sanitize(glsl));
    }

    // MemberData (not InlineData) because some built-in shaders are composed at runtime
    // (static readonly), not compile-time constants — those cannot be attribute arguments.
    public static IEnumerable<object[]> BuiltInShaderSources => new[]
    {
        new object[] { LayeredQuadShaderSource.Vertex },
        new object[] { LayeredQuadShaderSource.Fragment },
        new object[] { VuMeterAddon.FragmentShader },
        new object[] { PsyFractalVisualizerAddon.FragmentShader },
    };

    [Theory]
    [MemberData(nameof(BuiltInShaderSources))]
    public void BuiltIn_shader_sources_are_ascii_only(string shader)
    {
        // Regression guard: a single non-ASCII byte (e.g. an em-dash in a comment) silently breaks the
        // whole compositor on Intel GL. Keep the built-in shader sources strictly ASCII.
        char[] nonAscii = shader.Where(ch => ch > '\x7F').ToArray();
        Assert.True(
            nonAscii.Length == 0,
            $"Shader contains non-ASCII: {string.Join(", ", nonAscii.Select(c => $"U+{(int)c:X4}"))}");
    }
}
