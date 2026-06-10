using System;
using Liveolator.Core.Settings;
using Liveolator.Visuals.Gl;
using Xunit;

namespace Liveolator.Visuals.Tests.Gl;

public sealed class VuMeterFaceSpecTests
{
    [Theory]
    [InlineData(VuMeterNeedleOrigin.Bottom)]
    [InlineData(VuMeterNeedleOrigin.Top)]
    public void FaceSpec_MatchesGeometry(VuMeterNeedleOrigin origin)
    {
        // Drift guard: the spec shown to face authors must equal what the shader/face renderer use.
        VuMeterFaceSpec spec = VuMeterAddon.FaceSpec(origin);

        Assert.Equal(VuMeterGeometry.FaceWidth, spec.RecommendedWidth);
        Assert.Equal(VuMeterGeometry.FaceHeight, spec.RecommendedHeight);
        Assert.Equal(VuMeterGeometry.PivotXFrac, spec.PivotXFraction, precision: 5);
        Assert.Equal(VuMeterGeometry.PivotYFrac(origin), spec.PivotYFraction, precision: 5);
        Assert.Equal((int)Math.Round(VuMeterGeometry.PivotXPx), spec.PivotXPixels);
        Assert.Equal((int)Math.Round(VuMeterGeometry.PivotYPx(origin)), spec.PivotYPixels);
        Assert.Equal(VuMeterGeometry.ArcRadiusFrac, spec.ArcRadiusFraction, precision: 5);
        Assert.Equal((int)Math.Round(VuMeterGeometry.ArcRadiusPx), spec.ArcRadiusPixels);
        Assert.Equal(origin, spec.Origin);
    }

    [Fact]
    public void FaceSpec_HasExpectedPublishedValues()
    {
        // 1200x800, 3:2, centred hub. Bottom hub low (624 px), Top hub high (176 px) — exact mirror.
        Assert.Equal(1200, VuMeterAddon.FaceSpec().RecommendedWidth);
        Assert.Equal(800, VuMeterAddon.FaceSpec().RecommendedHeight);
        Assert.Equal(600, VuMeterAddon.FaceSpec().PivotXPixels);
        Assert.Equal(624, VuMeterAddon.FaceSpec(VuMeterNeedleOrigin.Bottom).PivotYPixels);
        Assert.Equal(176, VuMeterAddon.FaceSpec(VuMeterNeedleOrigin.Top).PivotYPixels);
        Assert.Equal(1.5, VuMeterAddon.FaceSpec().AspectRatio, precision: 5);
    }
}
