using Liveolator.Core.Beat;
using Xunit;

namespace Liveolator.Core.Tests.Beat;

public class TapTempoServiceTests
{
    private const long Ms = 1000; // ticks per second → timestamps in milliseconds

    private static TapTempoService Service(int maxTaps = 8) => new(Ms, maxTaps);

    [Fact]
    public void TwoTaps_500msApart_Give120Bpm()
    {
        var service = Service();
        service.Tap(0);
        service.Tap(500);

        Assert.True(service.TryGetBpm(out double bpm));
        Assert.Equal(120, bpm, precision: 6);
    }

    [Fact]
    public void SeveralTaps_AverageTheInterval()
    {
        var service = Service();
        foreach (long t in new long[] { 0, 400, 800, 1200 })
            service.Tap(t);

        Assert.True(service.TryGetBpm(out double bpm));
        Assert.Equal(150, bpm, precision: 6); // 400 ms interval
    }

    [Fact]
    public void FewerThanTwoTaps_HaveNoTempo()
    {
        var service = Service();
        service.Tap(0);

        Assert.False(service.HasTempo);
        Assert.False(service.TryGetBpm(out _));
    }

    [Fact]
    public void LongGap_StartsAFreshSeries()
    {
        var service = Service();
        service.Tap(0);
        service.Tap(5000); // > 2 s default reset gap

        Assert.Equal(1, service.TapCount);
        Assert.False(service.HasTempo);
    }

    [Fact]
    public void NonMonotonicTap_IsIgnored()
    {
        var service = Service();
        service.Tap(0);
        service.Tap(500);
        service.Tap(300); // out of order

        Assert.Equal(2, service.TapCount);
        Assert.True(service.TryGetBpm(out double bpm));
        Assert.Equal(120, bpm, precision: 6);
    }

    [Fact]
    public void Window_KeepsOnlyTheMostRecentTaps()
    {
        var service = Service(maxTaps: 2);
        service.Tap(0);
        service.Tap(500);
        service.Tap(1000); // window now [500, 1000]

        Assert.Equal(2, service.TapCount);
        Assert.True(service.TryGetBpm(out double bpm));
        Assert.Equal(120, bpm, precision: 6);
    }

    [Fact]
    public void Reset_ClearsTaps()
    {
        var service = Service();
        service.Tap(0);
        service.Tap(500);

        service.Reset();

        Assert.Equal(0, service.TapCount);
        Assert.False(service.HasTempo);
    }

    [Fact]
    public void Constructor_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TapTempoService(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TapTempoService(Ms, maxTaps: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TapTempoService(Ms, resetGapSeconds: 0));
    }
}
