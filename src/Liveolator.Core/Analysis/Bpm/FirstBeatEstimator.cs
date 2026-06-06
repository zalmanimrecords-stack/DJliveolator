namespace Liveolator.Core.Analysis.Bpm;

/// <summary>
/// Estimates a track's first-beat (downbeat) anchor — the within-beat offset, in seconds, where the
/// beat grid starts — from an onset envelope and a known tempo. Tempo gives the beat <em>period</em>;
/// this third stage gives the beat <em>phase</em>, which Quantize/phase-match needs to align two decks
/// (doc 03 beat-distance, doc 11). Pure and hardware-free (doc 16).
/// </summary>
/// <remarks>
/// Folds the onset envelope over one beat period and picks the phase bin carrying the most onset
/// energy: that bin is where beats land. Reported as an offset in [0, beatSeconds) from the track
/// start, so consumers add whole beats from there to reach any beat boundary.
/// </remarks>
public sealed class FirstBeatEstimator
{
    /// <summary>
    /// The first-beat offset in seconds, in [0, 60/bpm). Returns 0 when the envelope is empty or the
    /// tempo/rate is non-positive (no grid to phase-align).
    /// </summary>
    /// <param name="envelope">The onset-detection envelope (one value per analysis frame).</param>
    /// <param name="bpm">The detected tempo (BPM).</param>
    /// <param name="envelopeRateHz">Envelope frames per second (from <see cref="OnsetEnvelope.EnvelopeRateHz"/>).</param>
    public double Estimate(double[] envelope, double bpm, double envelopeRateHz)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Length == 0 || bpm <= 0.0 || envelopeRateHz <= 0.0)
            return 0.0;

        double framesPerBeat = 60.0 * envelopeRateHz / bpm;
        int period = (int)Math.Round(framesPerBeat);
        if (period < 1)
            return 0.0;

        // Fold every frame onto its phase bin within one beat; the bin with the most onset energy is
        // where beats land. Integer binning keeps this O(n) and free of windowing artefacts.
        var phaseEnergy = new double[period];
        for (int i = 0; i < envelope.Length; i++)
        {
            double value = envelope[i];
            if (value > 0.0)
                phaseEnergy[i % period] += value;
        }

        int bestBin = 0;
        double bestEnergy = phaseEnergy[0];
        for (int bin = 1; bin < period; bin++)
        {
            if (phaseEnergy[bin] > bestEnergy)
            {
                bestEnergy = phaseEnergy[bin];
                bestBin = bin;
            }
        }

        double offsetSeconds = bestBin / envelopeRateHz;
        double beatSeconds = 60.0 / bpm;
        // Guard the [0, beatSeconds) contract against the period-rounding (period may be a hair under a
        // beat), so a near-full-beat offset never spills past one beat.
        return offsetSeconds % beatSeconds;
    }
}
