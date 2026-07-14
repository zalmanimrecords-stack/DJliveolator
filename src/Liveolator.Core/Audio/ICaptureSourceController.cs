namespace Liveolator.Core.Audio;

/// <summary>
/// Applies the user's capture-source choice (doc 01 / doc 12 Settings) to the live pipeline: selecting
/// a device creates a capture source via <see cref="IAudioCaptureSourceFactory"/> and routes it into the
/// running chain; selecting "none" detaches it. Keeps the Settings UI free of the factory + pipeline
/// wiring and lets the selection round-trip through <see cref="Settings.AudioSettings"/>.
/// </summary>
/// <remarks>
/// A failure to open the chosen device must not throw — return false and let the caller surface it
/// (global standards #16/#26). Selecting null detaches any current capture source and always succeeds.
/// </remarks>
public interface ICaptureSourceController
{
    /// <summary>
    /// Route the given capture device into the live pipeline, replacing any current capture source, or
    /// detach the capture source when <paramref name="device"/> is null. Returns true on success; false
    /// if the device could not be opened (the prior source is left in place).
    /// </summary>
    bool SelectCaptureSource(AudioCaptureDevice? device);
}
