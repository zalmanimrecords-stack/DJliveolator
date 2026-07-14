namespace Liveolator.Core.Audio;

/// <summary>
/// Enumerates the capture endpoints available as live sources (doc 01): system-loopback feeds and
/// hardware line-inputs. The Settings / Live-tab device picker consumes this seam; the concrete,
/// platform-specific implementation (BASS on Win/macOS) lives in Liveolator.Audio, so Core and the
/// UI stay platform-independent and test against a fake.
/// </summary>
/// <remarks>
/// Enumeration must never throw on a missing backend or a transient device error — return an empty
/// list and let the caller surface "no capture devices" rather than crashing the UI (global
/// standards #16, #26). Re-querying picks up hot-plugged devices.
/// </remarks>
public interface IAudioCaptureDeviceCatalog
{
    /// <summary>
    /// Snapshot of the currently available capture endpoints. May be empty (no devices, or the
    /// native backend is absent). Never null, never throws.
    /// </summary>
    IReadOnlyList<AudioCaptureDevice> EnumerateCaptureDevices();
}
