using Liveolator.Audio.Playback;
using Liveolator.Core.Audio;

namespace Liveolator.Audio.Capture;

/// <summary>
/// Thin seam over the native BASS(-WASAPI/record) calls a capture source needs, so
/// <see cref="CaptureAudioSource"/> can be unit-tested with a fake while the real P/Invoke
/// (<see cref="BassCaptureBackend"/>) is isolated. Internal: a binding implementation detail, not a
/// public contract. Mirrors <c>IBassPlayback</c> for the playback path.
/// </summary>
internal interface ICaptureBackend : IDisposable
{
    /// <summary>
    /// Open the given capture endpoint and begin delivering interleaved float samples to
    /// <paramref name="onInterleavedSamples"/> on the capture thread. Returns the device's native
    /// channel format. Throws <see cref="BassCaptureException"/> on failure.
    /// </summary>
    BassChannelInfo Start(AudioCaptureDevice device, Action<float[]> onInterleavedSamples);

    /// <summary>Stop delivering samples for an open capture. Idempotent at the backend level.</summary>
    void Stop();
}
