namespace Liveolator.Core.Audio;

/// <summary>
/// A batch of raw, source-native samples emitted by an <see cref="IAudioSource"/> (doc 01).
/// Samples are interleaved by channel (L,R,L,R,… for stereo) at the source's native rate.
/// </summary>
/// <param name="Interleaved">Interleaved float samples; length is a whole number of frames (Channels each).</param>
/// <param name="Channels">Channel count (1 = mono, 2 = stereo).</param>
/// <param name="SampleRate">Source-native sample rate in Hz.</param>
public sealed record AudioSamplesAvailable(
    ReadOnlyMemory<float> Interleaved,
    int Channels,
    int SampleRate);
