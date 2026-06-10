using System;
using Liveolator.Visuals.Gl;
using Xunit;

namespace Liveolator.Visuals.Tests.Gl;

public sealed class VuMeterFaceSpecTests
{
    [Fact]
    public void FaceSpec_MatchesGeometry()
    {
        // Drift guard: the spec shown to face authors must equal what the shader/face renderer use.
        VuMeterFaceSpec spec = VuMeterAddon.FaceSpec;

        Assert.Equal(VuMeterGeometry.FaceWidth, spec.RecommendedWidth);
        Assert.Equal(VuMeterGeometry.FaceHeight, spec.RecommendedHeight);
        Assert.Equal(VuMeterGeometry.PivotXFrac, spec.PivotXFraction, precision: 5);
        Assert.Equal(VuMeterGeometry.PivotYFrac, spec.PivotYFraction, precision: 5);
        Assert.Equal((int)Math.Round(VuMeterGeometry.PivotXPx), spec.PivotXPixels);
        Assert.Equal((int)Math.Round(VuMeterGeometry.PivotYPx), spec.PivotYPixels);
        Assert.Equal(VuMeterGeometry.ArcRadiusFrac, spec.ArcRadiusFraction, precision: 5);
        Assert.Equal((int)Math.Round(VuMeterGeometry.ArcRadiusPx), spec.ArcRadiusPixels);
        Assert.Equal(VuMeterGeometry.NeedleMinDeg, spec.NeedleMinDegrees, precision: 5);
        Assert.Equal(VuMeterGeometry.NeedleMaxDeg, spec.NeedleMaxDegrees, precision: 5);
    }

    [Fact]
    public void FaceSpec_HasExpectedPublishedValues()
    {
        // The concrete values the Add-ons settings page documents (1200x800, pivot at 600x576, 3:2).
        VuMeterFaceSpec spec = VuMeterAddon.FaceSpec;

        Assert.Equal(1200, spec.RecommendedWidth);
        Assert.Equal(800, spec.RecommendedHeight);
        Assert.Equal(600, spec.PivotXPixels);
        Assert.Equal(160, spec.PivotYPixels); // hub near the TOP (20% down) — the needle hangs down
        Assert.Equal(1.5, spec.AspectRatio, precision: 5);
    }
}
