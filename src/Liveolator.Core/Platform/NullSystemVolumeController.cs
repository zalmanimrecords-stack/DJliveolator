namespace Liveolator.Core.Platform;

/// <summary>
/// The no-op <see cref="ISystemVolumeController"/> used when the OS master volume cannot be controlled
/// (unsupported platform, or controller construction failed). It reports <see cref="IsAvailable"/> = false
/// so the UI disables the global volume knob, and silently ignores writes — keeping the app fully usable
/// as a fallback (global standard #26: degrade, never crash).
/// </summary>
public sealed class NullSystemVolumeController : ISystemVolumeController
{
    /// <summary>Shared instance — the controller holds no state.</summary>
    public static readonly NullSystemVolumeController Instance = new();

    public bool IsAvailable => false;

    public double GetVolume() => 0.0;

    public void SetVolume(double level)
    {
        // Intentionally no-op: there is no OS volume target on this host.
    }
}
