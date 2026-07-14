using Liveolator.Core.Dsp;

namespace Liveolator.Core.Analysis.Bpm;

/// <summary>
/// Computes a spectral-flux onset-detection envelope from mono PCM: for each frame it sums
/// only the positively-changing magnitude bins relative to the previous frame. This is the
/// first stage of the BPM pipeline (doc 03 / doc 16).
/// </summary>
public sealed class OnsetEnvelope
{
    private readonly int _frameSize;
    private readonly int _hop;
    private readonly double[] _window;

    public OnsetEnvelope(int frameSize = 1024, int hop = 512)
    {
        if (frameSize < 2 || (frameSize & (frameSize - 1)) != 0)
            throw new ArgumentException("frameSize must be a power of two >= 2.", nameof(frameSize));
        if (hop < 1 || hop > frameSize)
            throw new ArgumentOutOfRangeException(nameof(hop), "hop must be in [1, frameSize].");

        _frameSize = frameSize;
        _hop = hop;
        _window = Window.Hann(frameSize);
    }

    /// <summary>Envelope samples per second for a given audio sample rate.</summary>
    public double EnvelopeRateHz(int sampleRate) => (double)sampleRate / _hop;

    /// <summary>
    /// Returns the onset envelope (one value per analysis frame). Empty if the signal is
    /// shorter than one frame.
    /// </summary>
    public double[] Compute(ReadOnlySpan<float> mono)
    {
        if (mono.Length < _frameSize)
            return Array.Empty<double>();

        int frames = 1 + (mono.Length - _frameSize) / _hop;
        var flux = new double[frames];
        var frame = new double[_frameSize];
        double[]? prevMag = null;

        for (int f = 0; f < frames; f++)
        {
            int start = f * _hop;
            for (int i = 0; i < _frameSize; i++)
                frame[i] = mono[start + i] * _window[i];

            double[] mag = Fft.MagnitudeSpectrum(frame);
            if (prevMag is not null)
            {
                double sum = 0.0;
                for (int i = 0; i < mag.Length; i++)
                {
                    double diff = mag[i] - prevMag[i];
                    if (diff > 0.0)
                        sum += diff;
                }
                flux[f] = sum;
            }
            prevMag = mag;
        }

        return flux;
    }
}
