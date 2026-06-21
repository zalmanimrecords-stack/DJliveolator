namespace Liveolator.Core.Analysis.Bpm;

/// <summary>
/// The bar-level anchor for a track: where the <em>downbeat</em> (beat 1 of the bar) lands, in seconds.
/// </summary>
/// <param name="DownbeatSeconds">
/// Offset of the first downbeat from track start, in [0, beatsPerBar · 60/bpm). 0 when undetectable.
/// </param>
/// <param name="BeatsPerBar">The assumed meter (4 for 4/4).</param>
/// <param name="Confidence">
/// 0..1 dominance of the chosen bar phase over a flat distribution. Low (≈0) when every beat carries
/// equal energy — four-on-the-floor is genuinely ambiguous at the bar level, the same half-bar
/// ambiguity the tempo stage has at the beat level, so consumers gate on this rather than trusting a
/// guessed downbeat (doc 03 — expose ambiguity, never hide it).
/// </param>
public sealed record DownbeatEstimate(double DownbeatSeconds, int BeatsPerBar, double Confidence)
{
    /// <summary>
    /// Conservative floor on <see cref="Confidence"/> above which consumers may TRUST the analyzed downbeat
    /// and lock to the bar; below it the bar is genuinely ambiguous (e.g. four-on-the-floor) and consumers
    /// fall back to beat-level alignment rather than a guessed downbeat (doc 03 — expose ambiguity). Shared
    /// so the STUDIO clip-snap and the DJ deck's bar-marker anchor gate on the same threshold.
    /// </summary>
    public const double ConfidenceFloor = 0.5;
}

/// <summary>
/// Estimates the downbeat from an onset envelope, a known tempo, and the beat-phase anchor. Where
/// <see cref="FirstBeatEstimator"/> answers "where do beats land", this answers "which beat starts the
/// bar": it sums onset energy per bar-relative beat position and picks the strongest. Fed the
/// kick-focused <see cref="LowBandOnsetEnvelope"/>, the strongest position is the kick-on-1 of most
/// tracks. Pure and hardware-free (doc 16).
/// </summary>
public sealed class DownbeatEstimator
{
    /// <param name="envelope">The onset envelope (one value per analysis frame) — kick-band preferred.</param>
    /// <param name="bpm">The detected tempo (BPM).</param>
    /// <param name="envelopeRateHz">Envelope frames per second.</param>
    /// <param name="firstBeatSeconds">The within-beat phase anchor from <see cref="FirstBeatEstimator"/>.</param>
    /// <param name="beatsPerBar">Meter; defaults to 4/4.</param>
    public DownbeatEstimate Estimate(
        double[] envelope, double bpm, double envelopeRateHz, double firstBeatSeconds, int beatsPerBar = 4)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Length == 0 || bpm <= 0.0 || envelopeRateHz <= 0.0 || beatsPerBar < 1)
            return new DownbeatEstimate(0.0, beatsPerBar < 1 ? 4 : beatsPerBar, 0.0);

        double beatSeconds = 60.0 / bpm;
        double beatFrames = beatSeconds * envelopeRateHz;
        double firstBeatFrame = firstBeatSeconds * envelopeRateHz;

        // Fold onset energy straight onto bar-relative beat positions: each frame is attributed to its
        // nearest beat, and that beat's position within the bar (0..beatsPerBar-1) collects the energy.
        var phaseEnergy = new double[beatsPerBar];
        for (int i = 0; i < envelope.Length; i++)
        {
            double value = envelope[i];
            if (value <= 0.0)
                continue;

            int beat = (int)Math.Round((i - firstBeatFrame) / beatFrames);
            if (beat < 0)
                continue;

            phaseEnergy[beat % beatsPerBar] += value;
        }

        int bestPhase = 0;
        double bestEnergy = phaseEnergy[0];
        double total = phaseEnergy[0];
        for (int phase = 1; phase < beatsPerBar; phase++)
        {
            total += phaseEnergy[phase];
            if (phaseEnergy[phase] > bestEnergy)
            {
                bestEnergy = phaseEnergy[phase];
                bestPhase = phase;
            }
        }

        // Dominance over a flat distribution (1/beatsPerBar): 0 when every phase is equal (ambiguous),
        // approaching 1 as all energy concentrates on one beat of the bar.
        double uniform = 1.0 / beatsPerBar;
        double confidence = total > 0.0
            ? Math.Clamp((bestEnergy / total - uniform) / (1.0 - uniform), 0.0, 1.0)
            : 0.0;

        double barSeconds = beatsPerBar * beatSeconds;
        double downbeatSeconds = (firstBeatSeconds + bestPhase * beatSeconds) % barSeconds;
        return new DownbeatEstimate(downbeatSeconds, beatsPerBar, Math.Round(confidence, 4));
    }
}
