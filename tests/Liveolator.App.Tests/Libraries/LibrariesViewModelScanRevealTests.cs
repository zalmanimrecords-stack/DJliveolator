using Liveolator.App.Features.Libraries;

namespace Liveolator.App.Tests.Libraries;

/// <summary>
/// Guards the scan-reveal cadence: already-scanned tracks must appear from the very first file
/// (eager for the first dozen), then throttle to every 25 so a large catalog isn't rebuilt per file.
/// Regression for the report "scanned tracks don't show in the library until the whole scan finishes".
/// </summary>
public sealed class LibrariesViewModelScanRevealTests
{
    [Theory]
    [InlineData(0, false)]   // nothing processed yet — the restored list is already shown
    [InlineData(1, true)]    // first file must reveal immediately, not wait for 25
    [InlineData(7, true)]    // the reported case: 7 tracks in, the list should be filling
    [InlineData(12, true)]   // last of the eager window
    [InlineData(13, false)]  // then throttled…
    [InlineData(24, false)]
    [InlineData(25, true)]   // …refreshing on each batch of 25
    [InlineData(37, false)]
    [InlineData(50, true)]
    public void ShouldRevealDuringScan_isEagerThenThrottled(int done, bool expected)
        => Assert.Equal(expected, LibrariesViewModel.ShouldRevealDuringScan(done));
}
