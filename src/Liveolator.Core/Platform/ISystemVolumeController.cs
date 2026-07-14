namespace Liveolator.Core.Platform;

/// <summary>
/// Seam over the operating system's master output volume — the level that affects the WHOLE computer
/// (every application + system sounds), distinct from the app's own mix (the <c>IMixer</c> seam). The
/// concrete implementations live per-OS in <c>Liveolator.Platform</c> (WASAPI on Windows, CoreAudio via
/// <c>osascript</c> on macOS); Core only depends on this interface. Values are normalized to 0..1.
/// </summary>
public interface ISystemVolumeController
{
    /// <summary>
    /// True when this host can read and set the OS master volume. False on an unsupported platform or
    /// when no usable output device is present — callers should disable the control rather than error.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>The current OS master volume in 0..1, or 0 when <see cref="IsAvailable"/> is false.</summary>
    double GetVolume();

    /// <summary>
    /// Sets the OS master volume. <paramref name="level"/> is clamped to 0..1. A no-op when
    /// <see cref="IsAvailable"/> is false. Implementations must surface failures via their own logging
    /// and never throw to the caller (a volume change must not crash a performance).
    /// </summary>
    void SetVolume(double level);
}
