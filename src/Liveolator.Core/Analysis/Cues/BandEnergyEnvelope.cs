using Liveolator.Core.Dsp;

namespace Liveolator.Core.Analysis.Cues;

/// <summary>
/// Computes a per-frame band-energy envelope from mono PCM: each STFT frame is split into
/// low / mid / high bands (by Hz crossover) plus a broadband sum, giving the contours that
/// structural-cue detection uses to find drops, breakdowns and build-ups (doc 11/16 phrase
/// analysis). The low band is the kick/bass region — the single most reliable EDM-structure
/// signal. Pure and hardware-free; mirrors <see cref="Bpm.OnsetEnvelope"/>'s framing so the two
/// envelopes line up frame-for-frame.
/// </summary>
public sealed class BandEnergyEnvelope
{
    private readonly int _frameSize;
    private readonly int _hop;
    private readonly double[] _window;
    private readonly double _lowCrossoverHz;
    private readonly double _highCrossoverHz;

    /// <param name="frameSize">FFT frame size (power of two ≥ 2).</param>
    /// <param name="hop">Frame advance in samples, in [1, frameSize].</param>
    /// <param name="lowCrossoverHz">Upper edge of the low (kick/bass) band; default 200 Hz.</param>
    /// <param name="highCrossoverHz">Lower edge of the high (presence/air) band; default 2000 Hz.</param>
    public BandEnergyEnvelope(
        int frameSize = 1024, int hop = 512, double lowCrossoverHz = 200.0, double highCrossoverHz = 2000.0)
    {
        if (frameSize < 2 || (frameSize & (frameSize - 1)) != 0)
            throw new ArgumentException("frameSize must be a power of two >= 2.", nameof(frameSize));
        if (hop < 1 || hop > frameSize)
            throw new ArgumentOutOfRangeException(nameof(hop), "hop must be in [1, frameSize].");
        if (lowCrossoverHz <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(lowCrossoverHz), "Low crossover must be positive.");
        if (highCrossoverHz <= lowCrossoverHz)
            throw new ArgumentOutOfRangeException(
                nameof(highCrossoverHz), "High crossover must be above the low crossover.");

        _frameSize = frameSize;
        _hop = hop;
        _window = Window.Hann(frameSize);
        _lowCrossoverHz = lowCrossoverHz;
        _highCrossoverHz = highCrossoverHz;
    }

    /// <summary>Envelope frames per second for a given audio sample rate.</summary>
    public double FrameRateHz(int sampleRate) => (double)sampleRate / _hop;

    /// <summary>
    /// Computes the band-energy envelope. Returns <see cref="BandEnergyFrames.Empty"/> when the
    /// signal is shorter than one frame or the sample rate is non-positive.
    /// </summary>
    public BandEnergyFrames Compute(ReadOnlySpan<float> mono, int sampleRate)
    {
        if (sampleRate <= 0 || mono.Length < _frameSize)
            return BandEnergyFrames.Empty;

        // Per-bin centre frequency = bin * sampleRate / frameSize; turn the Hz crossovers into the
        // first mid-band bin and the first high-band bin so the band split is a pair of array slices.
        double binHz = (double)sampleRate / _frameSize;
        int bins = _frameSize / 2 + 1;
        int firstMidBin = Math.Clamp((int)Math.Ceiling(_lowCrossoverHz / binHz), 1, bins);
        int firstHighBin = Math.Clamp((int)Math.Ceiling(_highCrossoverHz / binHz), firstMidBin, bins);

        int frames = 1 + (mono.Length - _frameSize) / _hop;
        var low = new double[frames];
        var mid = new double[frames];
        var high = new double[frames];
        var broadband = new double[frames];
        var frame = new double[_frameSize];

        for (int f = 0; f < frames; f++)
        {
            int start = f * _hop;
            for (int i = 0; i < _frameSize; i++)
                frame[i] = mono[start + i] * _window[i];

            double[] mag = Fft.MagnitudeSpectrum(frame);

            // Bin 0 is DC — excluded from every band so a track's offset doesn't masquerade as bass.
            double lowSum = 0.0, midSum = 0.0, highSum = 0.0;
            for (int i = 1; i < firstMidBin; i++)
                lowSum += mag[i];
            for (int i = firstMidBin; i < firstHighBin; i++)
                midSum += mag[i];
            for (int i = firstHighBin; i < bins; i++)
                highSum += mag[i];

            low[f] = lowSum;
            mid[f] = midSum;
            high[f] = highSum;
            broadband[f] = lowSum + midSum + highSum;
        }

        return new BandEnergyFrames(low, mid, high, broadband, FrameRateHz(sampleRate));
    }
}
