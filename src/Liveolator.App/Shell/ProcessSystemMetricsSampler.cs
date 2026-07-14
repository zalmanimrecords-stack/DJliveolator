using System;
using System.Diagnostics;

namespace Liveolator.App.Shell;

/// <summary>
/// Cross-platform <see cref="ISystemMetricsSampler"/> over the current <see cref="Process"/>: CPU% is the
/// process's processor time consumed since the previous sample, normalized over wall time and all cores
/// (so a single saturated core on an 8-core box reads ~12%, full load ~100%); memory is the working set in
/// MB. Pure System.Diagnostics — works on Windows and macOS, no native code or platform branching.
/// </summary>
public sealed class ProcessSystemMetricsSampler : ISystemMetricsSampler
{
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly int _coreCount = Math.Max(1, Environment.ProcessorCount);
    private readonly Func<DateTime> _now;
    private DateTime _lastWall;
    private TimeSpan _lastCpu;

    /// <param name="now">Monotonic-ish clock for the wall-time delta; defaults to UTC now (injectable for tests).</param>
    public ProcessSystemMetricsSampler(Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.UtcNow);
        _lastWall = _now();
        _lastCpu = _process.TotalProcessorTime;
    }

    /// <inheritdoc />
    public SystemMetrics Sample()
    {
        _process.Refresh(); // refresh cached WorkingSet64

        DateTime nowWall = _now();
        TimeSpan nowCpu = _process.TotalProcessorTime;
        double wallMs = (nowWall - _lastWall).TotalMilliseconds;
        double cpuMs = (nowCpu - _lastCpu).TotalMilliseconds;
        _lastWall = nowWall;
        _lastCpu = nowCpu;

        double cpuPercent = wallMs > 0
            ? Math.Clamp(cpuMs / (wallMs * _coreCount) * 100.0, 0.0, 100.0)
            : 0.0;
        double memoryMb = _process.WorkingSet64 / (1024.0 * 1024.0);
        return new SystemMetrics(cpuPercent, memoryMb);
    }
}
