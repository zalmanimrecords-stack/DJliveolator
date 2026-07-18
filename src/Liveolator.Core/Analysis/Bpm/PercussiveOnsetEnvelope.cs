using Liveolator.Core.Dsp;
using Liveolator.Core.Waveform;

namespace Liveolator.Core.Analysis.Bpm;

/// <summary>
/// A kick-onset envelope built on <b>harmonic/percussive source separation (HPSS)</b> so a sustained
/// bassline / sub / 808 that sits in the kick band no longer registers as a kick. Where
/// <see cref="LowBandOnsetEnvelope"/> sums <em>all</em> energy under the crossover — and so conflates the
/// kick with any low note on the off-beat (the secondary cause of unsatisfying beat-sync, system review
/// 2026-06-27) — this isolates the percussive part first.
/// </summary>
/// <remarks>
/// Median-filtering HPSS (Fitzgerald 2010): on the magnitude spectrogram a <em>harmonic</em> estimate is
/// the median along TIME per frequency bin (steady tones survive), a <em>percussive</em> estimate is the
/// median along FREQUENCY per frame (broadband transients survive), and a soft Wiener mask
/// P²/(P²+H²) keeps only the percussive share. A kick is a vertical (broadband, brief) stroke → high P;
/// a bass note is a horizontal (narrow-band, sustained) streak → high H → masked out. The percussive
/// energy is then summed over the low band (≤ the kick crossover) and half-wave-rectified frame-to-frame,
/// exactly the contract <see cref="LowBandOnsetEnvelope"/> exposes, so the tempo/phase/downbeat stages are
/// unchanged. Pure and hardware-free (doc 16); offline analysis only — too heavy for the realtime clock,
/// which keeps its broadband path.
/// </remarks>
public sealed class PercussiveOnsetEnvelope : IKickOnsetEnvelope
{
    /// <summary>Low-band crossover (Hz) — shared with the waveform strip's kick band and the band detector.</summary>
    public const double CrossoverHz = WaveformBuilder.LowCrossoverHz;

    private readonly int _frameSize;
    private readonly int _hop;
    private readonly int _timeRadius;
    private readonly int _freqRadius;
    private readonly double[] _window;

    /// <param name="frameSize">STFT frame size (power of two).</param>
    /// <param name="hop">STFT hop in samples (≤ frameSize).</param>
    /// <param name="timeMedian">Harmonic median length along time (frames, odd) — how "sustained" a tone must be to be removed.</param>
    /// <param name="freqMedian">Percussive median length along frequency (bins, odd) — how "broadband" a strike must be to survive.</param>
    public PercussiveOnsetEnvelope(int frameSize = 1024, int hop = 512, int timeMedian = 17, int freqMedian = 17)
    {
        Stft.ValidateFrameParams(frameSize, hop);
        if (timeMedian < 1 || freqMedian < 1)
            throw new ArgumentOutOfRangeException(nameof(timeMedian), "median lengths must be >= 1.");

        _frameSize = frameSize;
        _hop = hop;
        _timeRadius = timeMedian / 2;
        _freqRadius = freqMedian / 2;
        _window = Window.Hann(frameSize);
    }

    public double EnvelopeRateHz(int sampleRate) => (double)sampleRate / _hop;

    public double AnalysisLatencySeconds(int sampleRate) =>
        sampleRate > 0 ? _frameSize / (2.0 * sampleRate) : 0.0;

    public double[] Compute(ReadOnlySpan<float> mono, int sampleRate)
    {
        if (sampleRate <= 2 * CrossoverHz || mono.Length < _frameSize)
            return Array.Empty<double>();

        int frames = Stft.FrameCount(mono.Length, _frameSize, _hop);
        double binHz = (double)sampleRate / _frameSize;
        int lowTopBin = Math.Min((int)(CrossoverHz / binHz), _frameSize / 2);
        // The percussive (frequency) median around a low bin reaches up by _freqRadius, so the spectrogram
        // must hold a few bins above the kick band — but nowhere near the full spectrum, keeping this cheap.
        int binsKept = Math.Min(lowTopBin + _freqRadius + 1, _frameSize / 2 + 1);

        // Magnitude spectrogram, low region only: spectro[frame * binsKept + bin].
        var spectro = new double[frames * binsKept];
        Stft.ForEachFrame(mono, _window, _hop, (f, mag) =>
        {
            int row = f * binsKept;
            for (int b = 0; b < binsKept; b++)
                spectro[row + b] = mag[b];
        });

        var energy = new double[frames];
        var timeWindow = new double[2 * _timeRadius + 1];
        var freqWindow = new double[2 * _freqRadius + 1];
        for (int f = 0; f < frames; f++)
        {
            double sum = 0.0;
            for (int b = 0; b <= lowTopBin; b++)
            {
                double s = spectro[f * binsKept + b];
                if (s <= 0.0)
                    continue;

                double harmonic = MedianAlongTime(spectro, frames, binsKept, f, b, timeWindow);
                double percussive = MedianAlongFreq(spectro, binsKept, f, b, freqWindow);
                // Wiener-style soft mask: the percussive share of this time-frequency cell.
                double denom = percussive * percussive + harmonic * harmonic;
                double mask = denom > 0.0 ? percussive * percussive / denom : 0.0;
                double pe = s * mask;
                sum += pe * pe;
            }
            energy[f] = sum;
        }

        var flux = new double[frames];
        double previous = 0.0;
        for (int f = 0; f < frames; f++)
        {
            double rise = energy[f] - previous;
            flux[f] = rise > 0.0 ? rise : 0.0; // only rising percussive energy is an onset
            previous = energy[f];
        }

        return flux;
    }

    // Median of bin b across a time window centred on frame f (the harmonic estimate: steady over time).
    private double MedianAlongTime(
        double[] spectro, int frames, int binsKept, int f, int b, double[] scratch)
    {
        int count = 0;
        for (int t = f - _timeRadius; t <= f + _timeRadius; t++)
        {
            if (t < 0 || t >= frames)
                continue;
            scratch[count++] = spectro[t * binsKept + b];
        }
        return Median(scratch, count);
    }

    // Median of frame f across a frequency window centred on bin b (the percussive estimate: broadband now).
    private double MedianAlongFreq(double[] spectro, int binsKept, int f, int b, double[] scratch)
    {
        int row = f * binsKept;
        int count = 0;
        for (int k = b - _freqRadius; k <= b + _freqRadius; k++)
        {
            if (k < 0 || k >= binsKept)
                continue;
            scratch[count++] = spectro[row + k];
        }
        return Median(scratch, count);
    }

    private static double Median(double[] values, int count)
    {
        if (count == 0)
            return 0.0;
        Array.Sort(values, 0, count);
        int mid = count / 2;
        return (count & 1) == 1 ? values[mid] : 0.5 * (values[mid - 1] + values[mid]);
    }
}
