using Liveolator.Core.Waveform;

namespace Liveolator.Core.Analysis.Bpm;

/// <summary>
/// A kick-focused onset-detection envelope: the signal is split to its low band (≤200 Hz, the kick
/// fundamental + punch) with a 4th-order Linkwitz-Riley low-pass, then each frame reports the
/// <em>rise</em> in low-band energy over the previous frame (half-wave-rectified energy flux). Where the
/// broadband <see cref="OnsetEnvelope"/> fires on every transient — hats, vocals, synth stabs — this
/// fires on kicks, so the downbeat/grid stages can phase-align to the beat anchor rather than to
/// whatever happens to be loudest (doc 03 beat-distance). Pure and hardware-free (doc 16).
/// </summary>
/// <remarks>
/// Reuses the same LR4 split (two cascaded Butterworth sections, −24 dB/oct) and crossover the waveform
/// strip bands on, so the low band that draws as the on-screen "kick" layer and the band the beat grid
/// locks to are one and the same — what the performer sees is what the clock follows.
/// </remarks>
public sealed class LowBandOnsetEnvelope : IKickOnsetEnvelope
{
    /// <summary>Low-band crossover (Hz) — shared with the waveform strip's kick band.</summary>
    public const double CrossoverHz = WaveformBuilder.LowCrossoverHz;

    private readonly int _frameSize;
    private readonly int _hop;

    public LowBandOnsetEnvelope(int frameSize = 1024, int hop = 512)
    {
        if (frameSize < 2 || (frameSize & (frameSize - 1)) != 0)
            throw new ArgumentException("frameSize must be a power of two >= 2.", nameof(frameSize));
        if (hop < 1 || hop > frameSize)
            throw new ArgumentOutOfRangeException(nameof(hop), "hop must be in [1, frameSize].");

        _frameSize = frameSize;
        _hop = hop;
    }

    /// <summary>Envelope samples per second for a given audio sample rate (matches <see cref="OnsetEnvelope"/>).</summary>
    public double EnvelopeRateHz(int sampleRate) => (double)sampleRate / _hop;

    /// <summary>
    /// The analysis latency (seconds) to ADD back to a time derived from this envelope so it lands on the
    /// true onset. Each frame's energy represents audio centred ~frameSize/2 after the frame start, but a
    /// consumer maps frame index <c>f</c> to time <c>f/rate</c> (the frame start), reporting onsets ~half a
    /// frame early. Used by the beat-phase anchor so the grid lands ON the kick rather than a frame ahead of it.
    /// </summary>
    public double AnalysisLatencySeconds(int sampleRate) =>
        sampleRate > 0 ? _frameSize / (2.0 * sampleRate) : 0.0;

    /// <summary>
    /// Returns the low-band onset envelope (one value per analysis frame). Empty when the signal is
    /// shorter than one frame, or when the crossover doesn't sit under Nyquist (e.g. a very low decode
    /// rate) — the band degrades away rather than designing an unstable filter, mirroring the waveform
    /// band split's contract.
    /// </summary>
    public double[] Compute(ReadOnlySpan<float> mono, int sampleRate)
    {
        if (sampleRate <= 2 * CrossoverHz || mono.Length < _frameSize)
            return Array.Empty<double>();

        // Run the whole signal through the LR4 low-pass once (filter state is carried sample-serially,
        // a true running filter), then frame the filtered output. Buffering the filtered signal is what
        // lets overlapping frames (hop < frameSize) reuse samples without re-filtering.
        var filtered = new float[mono.Length];
        var chain = new[]
        {
            BiquadFilter.LowPass(CrossoverHz, sampleRate),
            BiquadFilter.LowPass(CrossoverHz, sampleRate),
        };
        for (int i = 0; i < mono.Length; i++)
            filtered[i] = chain[1].Process(chain[0].Process(mono[i]));

        int frames = 1 + (mono.Length - _frameSize) / _hop;
        var flux = new double[frames];
        double previousEnergy = 0.0;
        for (int f = 0; f < frames; f++)
        {
            int start = f * _hop;
            double sumSquares = 0.0;
            for (int i = 0; i < _frameSize; i++)
            {
                double sample = filtered[start + i];
                sumSquares += sample * sample;
            }

            double energy = sumSquares / _frameSize;
            double rise = energy - previousEnergy;
            // Only rising low-band energy is an onset; a decaying kick tail must not register as a beat.
            flux[f] = rise > 0.0 ? rise : 0.0;
            previousEnergy = energy;
        }

        return flux;
    }
}
