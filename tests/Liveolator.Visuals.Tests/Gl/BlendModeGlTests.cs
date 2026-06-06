using Liveolator.Core.Visuals;
using Liveolator.Visuals.Gl;
using Silk.NET.OpenGL;

namespace Liveolator.Visuals.Tests.Gl;

public sealed class BlendModeGlTests
{
    [Theory]
    [InlineData(BlendMode.Normal, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha)]
    [InlineData(BlendMode.Add, BlendingFactor.One, BlendingFactor.One)]
    [InlineData(BlendMode.Screen, BlendingFactor.One, BlendingFactor.OneMinusSrcColor)]
    [InlineData(BlendMode.Multiply, BlendingFactor.DstColor, BlendingFactor.OneMinusSrcAlpha)]
    public void Resolve_maps_separable_modes_to_premultiplied_factors(
        BlendMode mode, BlendingFactor expectedSrc, BlendingFactor expectedDst)
    {
        BlendModeGl gl = BlendModeGl.Resolve(mode);

        Assert.Equal(expectedSrc, gl.SourceFactor);
        Assert.Equal(expectedDst, gl.DestFactor);
        Assert.Equal(BlendEquationModeEXT.FuncAdd, gl.Equation);
    }

    [Fact]
    public void TryResolve_returns_false_for_overlay_the_only_nonseparable_mode()
    {
        Assert.False(BlendModeGl.TryResolve(BlendMode.Overlay, out _));
    }

    [Theory]
    [InlineData(BlendMode.Normal)]
    [InlineData(BlendMode.Add)]
    [InlineData(BlendMode.Screen)]
    [InlineData(BlendMode.Multiply)]
    public void TryResolve_returns_true_for_separable_modes(BlendMode mode)
    {
        Assert.True(BlendModeGl.TryResolve(mode, out _));
    }

    [Fact]
    public void Resolve_throws_for_a_nonseparable_mode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BlendModeGl.Resolve(BlendMode.Overlay));
    }
}
