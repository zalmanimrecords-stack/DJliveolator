namespace Liveolator.Core.Beat;

/// <summary>
/// A monotonic host-time source. Injected (rather than read statically) so beat logic that needs
/// "now" stays deterministic and unit-testable; the production implementation uses a high-resolution
/// timer (doc 03).
/// </summary>
public interface IHostClock
{
    /// <summary>Tick resolution of <see cref="NowTicks"/>, in ticks per second.</summary>
    long TicksPerSecond { get; }

    /// <summary>The current host time in ticks; monotonic across the session.</summary>
    long NowTicks { get; }
}
