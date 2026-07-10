using Liveolator.Core.Audio;
using Liveolator.Core.Settings;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

public class JogBendTrackerTests
{
    // alpha = 1 makes the smoothed velocity equal the latest instantaneous sample, so the bend is
    // exactly predictable in these tests; EMA smoothing (alpha < 1) is asserted separately.
    private static JogWheelSettings Instant(double releaseMs = 120.0)
        => new(VelocityEmaAlpha: 1.0, ReleaseTimeoutMs: releaseMs);

    [Fact]
    public void SteadyForwardTicks_ProduceAPositiveBend_UpToTheRail()
    {
        var tracker = new JogBendTracker(Instant());
        tracker.OnJog(0.01, nowSeconds: 0.000);       // first tick seeds the estimator
        double bend = tracker.OnJog(0.01, nowSeconds: 0.005); // 0.01 rev / 5 ms = 2 rev/s

        // 2 rev/s * 0.04 = 0.08, clamped at the ±8 % rail.
        Assert.Equal(0.08, bend, precision: 6);
    }

    [Fact]
    public void ReverseTicks_BendNegative()
    {
        var tracker = new JogBendTracker(Instant());
        tracker.OnJog(-0.01, nowSeconds: 0.000);
        double bend = tracker.OnJog(-0.01, nowSeconds: 0.010); // -0.01 / 10 ms = -1 rev/s

        Assert.Equal(-0.04, bend, precision: 6);
    }

    [Fact]
    public void ASingleSlowTick_StaysWithinTheDeadzone_AndDoesNotBend()
    {
        // One isolated tick is seeded against the max dt (100 ms): 0.0078 rev / 0.1 s = 0.078 rev/s,
        // just above the 0.05 deadzone, so it bends only a hair — never a jump.
        var tracker = new JogBendTracker(Instant());

        double bend = tracker.OnJog(0.003, nowSeconds: 0.0); // 0.003 / 0.1 = 0.03 rev/s < deadzone

        Assert.Equal(0.0, bend, precision: 6);
    }

    [Fact]
    public void TryReleaseStale_IsFalseBeforeTimeout_TrueOnceAfter()
    {
        var tracker = new JogBendTracker(Instant(releaseMs: 120.0));
        tracker.OnJog(0.01, nowSeconds: 0.000);
        tracker.OnJog(0.01, nowSeconds: 0.005); // now bending, last tick at t = 0.005

        Assert.False(tracker.TryReleaseStale(0.005 + 0.119)); // ticks still "recent"
        Assert.True(tracker.TryReleaseStale(0.005 + 0.120));  // ticks stopped → release
        Assert.False(tracker.TryReleaseStale(0.005 + 0.500)); // released only once
    }

    [Fact]
    public void NoBend_NothingToRelease()
    {
        var tracker = new JogBendTracker(Instant());

        // Never jogged → not bending → the pump must not emit a spurious release.
        Assert.False(tracker.TryReleaseStale(10.0));
    }

    [Fact]
    public void AfterRelease_NextTurnStartsFresh()
    {
        var tracker = new JogBendTracker(Instant());
        tracker.OnJog(0.01, nowSeconds: 0.000);
        tracker.OnJog(0.01, nowSeconds: 0.005);
        Assert.True(tracker.TryReleaseStale(1.0));

        // A tick long after release is treated as a first tick again (seeded against max dt), not as a
        // huge-dt spike off the stale timestamp.
        double bend = tracker.OnJog(0.003, nowSeconds: 5.0);
        Assert.Equal(0.0, bend, precision: 6); // 0.03 rev/s < deadzone
    }

    [Fact]
    public void EmaSmoothing_DampensASingleFastSampleRelativeToInstant()
    {
        var smoothed = new JogBendTracker(new JogWheelSettings(VelocityEmaAlpha: 0.4));
        var instant = new JogBendTracker(new JogWheelSettings(VelocityEmaAlpha: 1.0));
        smoothed.OnJog(0.001, 0.0); // both seed slow
        instant.OnJog(0.001, 0.0);

        double smoothedBend = smoothed.OnJog(0.02, 0.005); // sudden fast sample
        double instantBend = instant.OnJog(0.02, 0.005);

        Assert.True(smoothedBend < instantBend); // EMA lags the spike
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonFiniteInput_IsIgnored(double bad)
    {
        var tracker = new JogBendTracker(Instant());

        double bend = tracker.OnJog(bad, nowSeconds: 0.0);
        Assert.Equal(0.0, bend, precision: 6);

        tracker.OnJog(0.01, nowSeconds: 0.0);
        Assert.False(tracker.TryReleaseStale(bad)); // a bad clock reading never forces a release
    }
}
