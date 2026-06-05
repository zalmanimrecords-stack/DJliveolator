namespace Liveolator.Core.Audio;

/// <summary>
/// Immutable per-frame analysis snapshot (doc 02). Produced by the frame pipeline and shared by
/// every consumer (beat engine, visuals) so they see identical audio. Being a record, consumers
/// on other threads read a consistent picture without locking.
/// </summary>
/// <param name="MonoPcm">Mono float PCM for analysis and the visual feed (one analysis frame).</param>
/// <param name="Spectrum">Magnitude spectrum (FFT), non-redundant bins (frameSize/2 + 1).</param>
/// <param name="Waveform">Downsampled waveform for UI/overlays.</param>
/// <param name="SampleRate">Sample rate of <paramref name="MonoPcm"/> in Hz.</param>
/// <param name="FrameIndex">Monotonically increasing frame counter (-1 for the empty frame).</param>
/// <param name="TimestampSeconds">Stream time of this frame's first sample, in seconds.</param>
public sealed record AudioFrameData(
    float[] MonoPcm,
    float[] Spectrum,
    float[] Waveform,
    int SampleRate,
    long FrameIndex,
    double TimestampSeconds)
{
    /// <summary>The neutral frame returned before any audio has been analysed.</summary>
    public static AudioFrameData Empty { get; } = new(
        Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>(),
        SampleRate: 0, FrameIndex: -1, TimestampSeconds: 0.0);
}
