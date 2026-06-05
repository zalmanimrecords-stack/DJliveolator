using Liveolator.Core.Beat;

namespace Liveolator.Core.Tests.Beat;

/// <summary>A host clock whose "now" tests set explicitly, for deterministic time-based behavior.</summary>
internal sealed class FakeHostClock : IHostClock
{
    public FakeHostClock(long ticksPerSecond = 1000) => TicksPerSecond = ticksPerSecond;

    public long TicksPerSecond { get; }

    public long NowTicks { get; set; }
}
