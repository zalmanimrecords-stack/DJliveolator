using Liveolator.Core.Dsp;

namespace Liveolator.Core.Audio;

/// <summary>
/// Pure analysis kernel (doc 02): a fixed-size mono PCM frame → (magnitude spectrum, downsampled
/// waveform). Applies a Hann window before the FFT to reduce spectral leakage. No audio I/O, so
/// it unit-tests deterministically without hardware. Reused by the live frame pipeline and any
/// offline consumer.
/// </summary>
public sealed class SpectrumAnalyzer
{
    private readonly double[] _window;
    private readonly double[] _frameBuffer;

    public SpectrumAnalyzer(int frameSize = 2048, int waveformPoints = 256)
    {
        if (frameSize < 2 || (frameSize & (frameSize - 1)) != 0)
            throw new ArgumentException("frameSize must be a power of two >= 2.", nameof(frameSize));
        if (waveformPoints < 1 || waveformPoints > frameSize)
            throw new ArgumentOutOfRangeException(nameof(waveformPoints), "waveformPoints must be in [1, frameSize].");

        FrameSize = frameSize;
        WaveformPoints = waveformPoints;
        SpectrumBins = frameSize / 2 + 1;
        _window = Window.Hann(frameSize);
        _frameBuffer = new double[frameSize];
    }

    /// <summary>The exact mono frame length <see cref="Analyze"/> expects.</summary>
    public int FrameSize { get; }

    /// <summary>Number of waveform points produced per frame.</summary>
    public int WaveformPoints { get; }

    /// <summary>Length of the spectrum produced per frame (frameSize/2 + 1).</summary>
    public int SpectrumBins { get; }

    /// <summary>
    /// Analyse one mono frame of exactly <see cref="FrameSize"/> samples. Not thread-safe: it
    /// reuses internal scratch buffers, so call it from a single analysis thread.
    /// </summary>
    public (float[] Spectrum, float[] Waveform) Analyze(ReadOnlySpan<float> monoFrame)
    {
        if (monoFrame.Length != FrameSize)
            throw new ArgumentException($"monoFrame must be exactly {FrameSize} samples, got {monoFrame.Length}.", nameof(monoFrame));

        for (int i = 0; i < FrameSize; i++)
            _frameBuffer[i] = monoFrame[i] * _window[i];

        double[] mag = Fft.MagnitudeSpectrum(_frameBuffer);
        var spectrum = new float[mag.Length];
        for (int i = 0; i < mag.Length; i++)
            spectrum[i] = (float)mag[i];

        return (spectrum, Downsample(monoFrame));
    }

    /// <summary>Block-average the frame down to <see cref="WaveformPoints"/> values (sign preserved).</summary>
    private float[] Downsample(ReadOnlySpan<float> monoFrame)
    {
        var waveform = new float[WaveformPoints];
        for (int p = 0; p < WaveformPoints; p++)
        {
            int start = (int)((long)p * FrameSize / WaveformPoints);
            int end = (int)((long)(p + 1) * FrameSize / WaveformPoints);
            if (end <= start) end = start + 1;

            double sum = 0.0;
            for (int i = start; i < end; i++)
                sum += monoFrame[i];
            waveform[p] = (float)(sum / (end - start));
        }
        return waveform;
    }
}
