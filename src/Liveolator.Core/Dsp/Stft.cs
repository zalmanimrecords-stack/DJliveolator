namespace Liveolator.Core.Dsp;

/// <summary>
/// Shared short-time Fourier transform (STFT) framing used by every spectral analyzer
/// (spectral-flux onset, band-energy, chroma, percussive HPSS). Centralizing the
/// power-of-two/hop validation, the frame count, and the Hann-windowed frame slide +
/// magnitude spectrum keeps their framing byte-for-byte aligned — several of these
/// envelopes are required to line up frame-for-frame (the cue and onset contours), and
/// hand-copied framing loops let that alignment drift. Pure and allocation-light.
/// </summary>
public static class Stft
{
    /// <summary>Validates the STFT frame parameters shared by every analyzer's constructor.</summary>
    public static void ValidateFrameParams(int frameSize, int hop)
    {
        if (frameSize < 2 || (frameSize & (frameSize - 1)) != 0)
            throw new ArgumentException("frameSize must be a power of two >= 2.", nameof(frameSize));
        if (hop < 1 || hop > frameSize)
            throw new ArgumentOutOfRangeException(nameof(hop), "hop must be in [1, frameSize].");
    }

    /// <summary>
    /// Number of full hop-advanced frames <paramref name="sampleCount"/> samples yield; 0 when the
    /// signal is shorter than one frame.
    /// </summary>
    public static int FrameCount(int sampleCount, int frameSize, int hop)
        => sampleCount < frameSize ? 0 : 1 + (sampleCount - frameSize) / hop;

    /// <summary>
    /// Slides a windowed frame across <paramref name="mono"/> and invokes <paramref name="onFrame"/>
    /// with each frame index and its magnitude spectrum. The magnitude array is freshly allocated per
    /// frame (via <see cref="Fft.MagnitudeSpectrum"/>), so a caller may retain it across frames (e.g.
    /// spectral flux comparing against the previous frame). <paramref name="window"/>'s length is the
    /// frame size; no callback fires for a signal shorter than one frame.
    /// </summary>
    public static void ForEachFrame(
        ReadOnlySpan<float> mono, double[] window, int hop, Action<int, double[]> onFrame)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(onFrame);

        int frameSize = window.Length;
        int frames = FrameCount(mono.Length, frameSize, hop);
        var frame = new double[frameSize];
        for (int f = 0; f < frames; f++)
        {
            int start = f * hop;
            for (int i = 0; i < frameSize; i++)
                frame[i] = mono[start + i] * window[i];
            onFrame(f, Fft.MagnitudeSpectrum(frame));
        }
    }
}
