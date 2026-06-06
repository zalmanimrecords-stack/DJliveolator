using Liveolator.Core.Settings;

namespace Liveolator.Core.Audio;

/// <summary>
/// Re-opens the realtime audio engine on a new output device / buffer at runtime (doc 12 Settings),
/// so a device change applies without an app restart. The pure seam lives in Core; the native BASS
/// re-open lives in Liveolator.Audio (verified manually). The same <see cref="AudioSettings"/>→BASS
/// mapping used at startup is reused — Core stays platform-independent and the trigger logic that
/// drives this seam (<see cref="AudioReinitCoordinator"/>) unit-tests against a fake.
/// </summary>
public interface IAudioEngineReinitializer
{
    /// <summary>
    /// Re-open the audio output with the given settings (device + buffer). Returns true if the engine
    /// is now running on the requested (or a safe fallback) device; false if re-init failed and audio
    /// could not be restored. Implementations must not throw for an expected device error — surface it
    /// as a return value so the coordinator can roll back; only truly unexpected faults propagate.
    /// </summary>
    bool Reinitialize(AudioSettings settings);
}
