using System.Diagnostics;

namespace Liveolator.Core.Beat;

/// <summary>
/// Host clock backed by the high-resolution monotonic <see cref="Stopwatch"/> timer — the default
/// in the running app. Pure managed BCL, no native or platform dependency.
/// </summary>
public sealed class SystemHostClock : IHostClock
{
    /// <inheritdoc />
    public long TicksPerSecond => Stopwatch.Frequency;

    /// <inheritdoc />
    public long NowTicks => Stopwatch.GetTimestamp();
}
