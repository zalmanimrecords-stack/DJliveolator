namespace Liveolator.Core.Audio;

/// <summary>
/// Enumerates the sound-card output endpoints available to drive the master mix (doc 01). The Settings
/// device picker consumes this seam; the concrete, platform-specific implementation (BASS on
/// Win/macOS) lives in Liveolator.Audio, so Core and the UI stay platform-independent and test against
/// a fake. Mirrors <see cref="IAudioCaptureDeviceCatalog"/> on the output side.
/// </summary>
/// <remarks>
/// Enumeration must never throw on a missing backend or a transient device error — return an empty
/// list and let the caller surface "no output devices" rather than crashing the UI (global standards
/// #16, #26). Re-querying picks up hot-plugged devices.
/// </remarks>
public interface IAudioOutputDeviceCatalog
{
    /// <summary>
    /// Snapshot of the currently available output endpoints. May be empty (no devices, or the native
    /// backend is absent). Never null, never throws.
    /// </summary>
    IReadOnlyList<AudioOutputDevice> EnumerateOutputDevices();
}
