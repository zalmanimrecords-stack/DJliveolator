namespace Liveolator.Core.Audio;

/// <summary>
/// A capture endpoint the user can select as a live source (doc 01): a system-loopback feed or a
/// hardware line-input. Identifies the device for the backend to open and carries enough detail for
/// the Settings/Live-tab picker to label it. Pure data — no native handle leaks into Core.
/// </summary>
/// <param name="Id">
/// Backend-opaque device identifier (e.g. a BASS device index encoded as a string). Stable enough
/// to persist a user's selection; the backend is responsible for resolving it.
/// </param>
/// <param name="Name">Human-readable device name for the picker.</param>
/// <param name="Kind">Whether this endpoint is a system-loopback feed or a hardware line-input.</param>
/// <param name="IsDefault">True if this is the platform's default endpoint of its kind.</param>
public sealed record AudioCaptureDevice(
    string Id,
    string Name,
    CaptureSourceKind Kind,
    bool IsDefault);
