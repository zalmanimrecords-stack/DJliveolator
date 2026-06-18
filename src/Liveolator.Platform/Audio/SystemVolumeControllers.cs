using Liveolator.Core.Platform;

namespace Liveolator.Platform.Audio;

/// <summary>
/// Picks the OS-appropriate <see cref="ISystemVolumeController"/> at the composition root: WASAPI on
/// Windows, <c>osascript</c> on macOS, and the no-op <see cref="NullSystemVolumeController"/> everywhere
/// else (or if the platform controller cannot be constructed) — so the global volume knob disables itself
/// gracefully instead of breaking the app.
/// </summary>
public static class SystemVolumeControllers
{
    /// <param name="onWarning">Optional diagnostic sink (wired to the app log at the root).</param>
    public static ISystemVolumeController Create(Action<string>? onWarning = null)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return new WindowsSystemVolumeController(onWarning);
            if (OperatingSystem.IsMacOS())
                return new MacSystemVolumeController(onWarning);
        }
        catch (Exception ex)
        {
            onWarning?.Invoke($"SystemVolumeControllers: falling back to no-op volume control. {ex.Message}");
        }

        return NullSystemVolumeController.Instance;
    }
}
