namespace Liveolator.Core.Audio;

/// <summary>
/// A sound-card output endpoint the user can select to drive the master mix (doc 01/11): a speaker
/// output or a DJ interface like the CMD STUDIO 2A. Mirrors <see cref="AudioCaptureDevice"/> on the
/// output side — pure data, no native handle leaks into Core.
/// </summary>
/// <param name="Id">
/// Backend-opaque device identifier (e.g. a BASS device index encoded as a string). Stable enough to
/// persist a user's selection; the backend resolves it back to a device when initialising output.
/// </param>
/// <param name="Name">Human-readable device name for the picker.</param>
/// <param name="IsDefault">True if this is the platform's default output endpoint.</param>
public sealed record AudioOutputDevice(
    string Id,
    string Name,
    bool IsDefault);
