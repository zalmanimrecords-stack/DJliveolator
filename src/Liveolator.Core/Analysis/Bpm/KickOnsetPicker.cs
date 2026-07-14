namespace Liveolator.Core.Analysis.Bpm;

/// <summary>
/// Peak-picks a kick-onset envelope (<see cref="IKickOnsetEnvelope"/>) into the list of kick STRIKE TIMES
/// (seconds from track start). Where <see cref="FirstBeatEstimator"/> collapses the whole track to one
/// within-beat phase, this keeps every individual kick so a deck can later snap its grid onto the real
/// kick nearest the playhead (the "SET PHASE" / one-shot SYNC auto-align, doc 11) instead of trusting a
/// global anchor that a drifting tempo can pull off the local kick. Pure and hardware-free (doc 16).
/// </summary>
public static class KickOnsetPicker
{
    // A kick must clear this fraction of the loudest kick to count — rejects the low-level ripple between
    // kicks without a per-track tuning knob (the envelope is already kick-isolated by HPSS upstream).
    private const double RelativeFloor = 0.15;

    // Kicks never fall closer than this in real music (240 BPM four-on-the-floor = 0.25 s apart); a shorter
    // refractory would double-pick the decay tail of one kick. Well below any real inter-kick spacing.
    private const double RefractorySeconds = 0.12;

    // Local-maximum half-window (seconds): a peak must dominate roughly this far either side, so a broad
    // hump yields one onset, not several.
    private const double PeakHalfWindowSeconds = 0.03;

    /// <summary>
    /// Kick strike times in seconds (ascending), latency-compensated so each lands on the true onset.
    /// Empty when the envelope is empty or the rate is non-positive (no kick band to pick).
    /// </summary>
    /// <param name="envelope">The kick-onset envelope, one value per analysis frame.</param>
    /// <param name="envelopeRateHz">Envelope frames per second.</param>
    /// <param name="analysisLatencySeconds">Seconds to add so a frame index maps to the true onset time
    /// (<see cref="IKickOnsetEnvelope.AnalysisLatencySeconds"/>).</param>
    public static IReadOnlyList<double> Pick(
        double[] envelope, double envelopeRateHz, double analysisLatencySeconds = 0.0)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Length == 0 || envelopeRateHz <= 0.0)
            return Array.Empty<double>();

        double max = 0.0;
        foreach (double v in envelope)
            if (v > max) max = v;
        if (max <= 0.0)
            return Array.Empty<double>();

        double floor = max * RelativeFloor;
        int half = Math.Max(1, (int)Math.Round(PeakHalfWindowSeconds * envelopeRateHz));
        int refractory = Math.Max(1, (int)Math.Round(RefractorySeconds * envelopeRateHz));

        var onsets = new List<double>();
        int lastAccepted = -refractory;
        for (int i = 0; i < envelope.Length; i++)
        {
            double value = envelope[i];
            if (value < floor || i - lastAccepted < refractory)
                continue;

            // Accept only a strict local maximum in the window so the decay shoulder of a kick is not
            // taken as a second onset (ties to the left are rejected, so a flat top picks its first frame).
            bool isPeak = true;
            int from = Math.Max(0, i - half);
            int to = Math.Min(envelope.Length - 1, i + half);
            for (int j = from; j <= to && isPeak; j++)
            {
                if (j < i && envelope[j] >= value) isPeak = false;
                else if (j > i && envelope[j] > value) isPeak = false;
            }
            if (!isPeak)
                continue;

            onsets.Add(i / envelopeRateHz + analysisLatencySeconds);
            lastAccepted = i;
        }

        return onsets;
    }
}
