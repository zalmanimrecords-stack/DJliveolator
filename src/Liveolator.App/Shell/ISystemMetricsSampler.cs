namespace Liveolator.App.Shell;

/// <summary>One reading of the app's resource use: CPU load (0..100%) and resident memory (MB).</summary>
public readonly record struct SystemMetrics(double CpuPercent, double MemoryMb);

/// <summary>
/// Samples the running app's CPU and memory use for the top-bar readout. A seam so the
/// <see cref="ShellStatusViewModel"/> stays UI-free and unit-testable with a fake sampler; the
/// real implementation (<see cref="ProcessSystemMetricsSampler"/>) reads the current process.
/// </summary>
public interface ISystemMetricsSampler
{
    /// <summary>Takes a reading; CPU% is the load since the previous call (0 on the first call).</summary>
    SystemMetrics Sample();
}
