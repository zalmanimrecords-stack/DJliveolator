using Liveolator.Core.Settings;
using Xunit;

namespace Liveolator.Core.Tests.Settings;

public class DiagnosticsSettingsTests
{
    [Fact]
    public void Default_IsWarning_SoOnlyWarningAndAboveAreKept()
        => Assert.Equal("Warning", DiagnosticsSettings.Default.MinimumLevel);

    [Fact]
    public void ParameterDefault_MatchesTheDeclaredConstant()
        => Assert.Equal(DiagnosticsSettings.DefaultMinimumLevel, new DiagnosticsSettings().MinimumLevel);

    [Theory]
    [InlineData("debug", "Debug")]       // case folded to canonical
    [InlineData("WARNING", "Warning")]
    [InlineData("Trace", "Trace")]       // already canonical
    public void Normalized_FoldsKnownLevelsToCanonicalCasing(string input, string expected)
        => Assert.Equal(expected, new DiagnosticsSettings(input).Normalized().MinimumLevel);

    [Theory]
    [InlineData("verbose")]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalized_UnknownOrBlankFallsBackToDefault(string input)
        => Assert.Equal(DiagnosticsSettings.DefaultMinimumLevel, new DiagnosticsSettings(input).Normalized().MinimumLevel);

    [Fact]
    public void AppSettings_Default_IncludesDiagnostics()
        => Assert.Equal(DiagnosticsSettings.Default, AppSettings.Default.Diagnostics);

    [Fact]
    public void AppSettings_Normalized_NormalizesDiagnostics()
    {
        AppSettings settings = AppSettings.Default with { Diagnostics = new DiagnosticsSettings("nonsense") };

        Assert.Equal(DiagnosticsSettings.DefaultMinimumLevel, settings.Normalized().Diagnostics.MinimumLevel);
    }
}
