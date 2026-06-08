using Liveolator.Core.Settings;
using Xunit;

namespace Liveolator.Core.Tests.Settings;

public class VisualsSettingsTests
{
    [Fact]
    public void Default_IsTheConfiguredDefaultZoom()
        => Assert.Equal(VisualsSettings.DefaultZoomSeconds, VisualsSettings.Default.WaveformZoomSeconds, precision: 6);

    [Theory]
    [InlineData(0.5, VisualsSettings.MinZoomSeconds)]   // below the floor → clamped up
    [InlineData(100.0, VisualsSettings.MaxZoomSeconds)] // above the ceiling → clamped down
    [InlineData(7.0, 7.0)]                              // in range → unchanged
    public void Normalized_ClampsIntoRange(double input, double expected)
        => Assert.Equal(expected, new VisualsSettings(input).Normalized().WaveformZoomSeconds, precision: 6);

    [Fact]
    public void Normalized_NaN_FallsBackToDefault()
        => Assert.Equal(
            VisualsSettings.DefaultZoomSeconds,
            new VisualsSettings(double.NaN).Normalized().WaveformZoomSeconds,
            precision: 6);

    [Fact]
    public void AppSettings_Normalized_NormalizesVisuals()
    {
        AppSettings settings = AppSettings.Default with { Visuals = new VisualsSettings(999.0) };

        Assert.Equal(VisualsSettings.MaxZoomSeconds, settings.Normalized().Visuals.WaveformZoomSeconds, precision: 6);
    }

    [Fact]
    public void Default_NudgeIsTheConfiguredDefault()
        => Assert.Equal(VisualsSettings.DefaultNudgeSeconds, VisualsSettings.Default.NudgeSeconds, precision: 6);

    [Theory]
    [InlineData(0.01, VisualsSettings.MinNudgeSeconds)] // below the floor → clamped up
    [InlineData(9.0, VisualsSettings.MaxNudgeSeconds)]  // above the ceiling → clamped down
    [InlineData(0.1, 0.1)]                              // in range → unchanged
    public void Normalized_ClampsNudgeIntoRange(double input, double expected)
        => Assert.Equal(
            expected,
            new VisualsSettings(WaveformZoomSeconds: 7.0, NudgeSeconds: input).Normalized().NudgeSeconds,
            precision: 6);
}
