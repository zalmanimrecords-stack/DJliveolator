using System.Collections.Generic;
using Liveolator.Core.Update;
using Xunit;

namespace Liveolator.Core.Tests.Update;

public class UpdateAvailabilityCheckerTests
{
    private static UpdateManifest Manifest(string version)
        => new(version, $"https://example.test/Setup-{version}.exe", new List<string> { "note" });

    [Fact]
    public void NewerVersion_IsOffered()
    {
        UpdateCheckResult result = UpdateAvailabilityChecker.Evaluate("0.1.4", Manifest("0.1.5"), skippedVersion: null);

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("0.1.5", result.Manifest!.Version);
    }

    [Theory]
    [InlineData("0.1.4", "0.1.4")] // same
    [InlineData("0.1.4", "0.1.3")] // older
    [InlineData("1.0.0", "0.9.9")] // older across major
    public void SameOrOlderVersion_IsNotOffered(string installed, string latest)
        => Assert.False(UpdateAvailabilityChecker.Evaluate(installed, Manifest(latest), null).IsUpdateAvailable);

    [Fact]
    public void NullManifest_IsNotOffered()
        => Assert.False(UpdateAvailabilityChecker.Evaluate("0.1.4", manifest: null, skippedVersion: null).IsUpdateAvailable);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    public void UnparsableInstalledVersion_IsNotOffered_ConservativeOnBadData(string? installed)
        => Assert.False(UpdateAvailabilityChecker.Evaluate(installed, Manifest("0.1.5"), null).IsUpdateAvailable);

    [Fact]
    public void UnparsableManifestVersion_IsNotOffered()
        => Assert.False(UpdateAvailabilityChecker.Evaluate("0.1.4", Manifest("garbage"), null).IsUpdateAvailable);

    [Fact]
    public void SkippedVersionEqualToLatest_IsNotOffered()
        => Assert.False(UpdateAvailabilityChecker.Evaluate("0.1.4", Manifest("0.1.5"), skippedVersion: "0.1.5").IsUpdateAvailable);

    [Fact]
    public void SkippedVersion_DoesNotSuppressAStillNewerBuild()
        => Assert.True(UpdateAvailabilityChecker.Evaluate("0.1.4", Manifest("0.1.6"), skippedVersion: "0.1.5").IsUpdateAvailable);

    [Theory]
    [InlineData("v0.1.5")]        // leading v
    [InlineData("0.1.5-beta")]    // pre-release suffix
    [InlineData("0.1.5+build7")]  // build metadata suffix
    public void VersionStrings_AreParsedTolerantly(string latest)
        => Assert.True(UpdateAvailabilityChecker.Evaluate("0.1.4", Manifest(latest), null).IsUpdateAvailable);

    [Fact]
    public void SkippedVersion_WithLeadingV_StillSuppressesTheMatchingBuild()
        // The skip value is parsed tolerantly too, so a "v"-prefixed skip still matches "0.1.5".
        => Assert.False(UpdateAvailabilityChecker.Evaluate("0.1.4", Manifest("0.1.5"), skippedVersion: "v0.1.5").IsUpdateAvailable);
}
