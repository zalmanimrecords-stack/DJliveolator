using Liveolator.Core.Visuals;
using Xunit;

namespace Liveolator.Core.Tests.Visuals;

public class VisualModelTests
{
    private static VisualLayer Layer(double opacity = 1.0)
        => new(
            "layer",
            new VisualSourceRef(VisualSourceKind.Image, "bg.png"),
            Array.Empty<EffectRef>(),
            BlendMode.Normal,
            opacity);

    private static VisualScene Scene(string name)
        => new(name, new[] { Layer() }, new Dictionary<string, double>(), TransitionStyle.Crossfade, BeatBehavior.None);

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void VisualLayer_RejectsOpacityOutOfRange(double opacity)
        => Assert.Throws<ArgumentOutOfRangeException>(() => Layer(opacity));

    [Fact]
    public void VisualBank_Scene_ReturnsSceneInRange()
    {
        var bank = new VisualBank("bank", new[] { Scene("a"), Scene("b") });

        Assert.Equal("b", bank.Scene(1)!.Name);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void VisualBank_Scene_ReturnsNullOutOfRange(int index)
    {
        var bank = new VisualBank("bank", new[] { Scene("a"), Scene("b") });

        Assert.Null(bank.Scene(index));
    }

    [Fact]
    public void VisualScene_RejectsNullLayers()
        => Assert.Throws<ArgumentNullException>(
            () => new VisualScene("s", null!, new Dictionary<string, double>(), TransitionStyle.Cut, BeatBehavior.None));

    [Fact]
    public void BeatBehavior_None_IsInert()
    {
        Assert.False(BeatBehavior.None.PulseOnBeat);
        Assert.False(BeatBehavior.None.PulseOnDownbeat);
        Assert.Equal(0, BeatBehavior.None.LaunchEveryBars);
    }
}
