namespace Liveolator.Core.Analysis.Bpm;

/// <summary>Final BPM measurement for a track.</summary>
/// <param name="Bpm">Detected tempo in BPM (0 when undetectable).</param>
/// <param name="Confidence">Detection confidence, 0..1.</param>
/// <param name="FirstBeatSeconds">
/// The first-beat (downbeat) anchor: the within-beat offset in seconds, in [0, 60/Bpm), where the beat
/// grid starts. Quantize/phase-match aligns decks against it (doc 11). 0 when tempo is undetectable.
/// Added after the original (Bpm, Confidence) shape with a default of 0 so existing consumers and
/// serialized catalogs are unaffected (positional-record back-compat).
/// </param>
public sealed record BpmResult(double Bpm, double Confidence, double FirstBeatSeconds = 0.0)
{
    /// <summary>
    /// The downbeat (beat 1) anchor: offset in seconds from track start to the first downbeat. Where
    /// <see cref="FirstBeatSeconds"/> fixes where beats land, this fixes where the <em>bar</em> starts,
    /// so bar/phrase-level alignment composes. 0 when undetectable. Init-only with a default so the
    /// positional (Bpm, Confidence, FirstBeatSeconds) shape and existing serialized catalogs are
    /// unaffected (positional-record back-compat).
    /// </summary>
    public double DownbeatSeconds { get; init; }

    /// <summary>The assumed meter; 4 for 4/4. Defaults to 4 for back-compat with pre-downbeat catalogs.</summary>
    public int BeatsPerBar { get; init; } = 4;

    /// <summary>
    /// 0..1 confidence in the downbeat placement. Low on four-on-the-floor (genuinely ambiguous at the
    /// bar level); consumers gate bar-level alignment on it rather than trusting a guessed downbeat.
    /// </summary>
    public double DownbeatConfidence { get; init; }
}

/// <summary>
/// Orchestrates the BPM pipeline: mono PCM → onset envelope → tempo estimate → first-beat anchor.
/// Pure and hardware-free; the decode that produces the PCM lives behind <c>IAudioDecoder</c>.
/// </summary>
public sealed class BpmDetector
{
    private readonly OnsetEnvelope _onset;
    private readonly TempoEstimator _tempo;
    private readonly FirstBeatEstimator _firstBeat;
    private readonly LowBandOnsetEnvelope _kickOnset;
    private readonly DownbeatEstimator _downbeat;

    public BpmDetector(
        OnsetEnvelope? onset = null,
        TempoEstimator? tempo = null,
        FirstBeatEstimator? firstBeat = null,
        LowBandOnsetEnvelope? kickOnset = null,
        DownbeatEstimator? downbeat = null)
    {
        _onset = onset ?? new OnsetEnvelope();
        _tempo = tempo ?? new TempoEstimator();
        _firstBeat = firstBeat ?? new FirstBeatEstimator();
        _kickOnset = kickOnset ?? new LowBandOnsetEnvelope();
        _downbeat = downbeat ?? new DownbeatEstimator();
    }

    public BpmResult Detect(ReadOnlySpan<float> mono, int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        double[] envelope = _onset.Compute(mono);
        if (envelope.Length == 0)
            return new BpmResult(0, 0);

        double envelopeRateHz = _onset.EnvelopeRateHz(sampleRate);
        TempoEstimate estimate = _tempo.Estimate(envelope, envelopeRateHz);
        double bpm = Math.Round(estimate.Bpm, 2);
        double firstBeatSeconds = Math.Round(_firstBeat.Estimate(envelope, bpm, envelopeRateHz), 4);

        // Downbeat uses the kick band, not the broadband envelope: beats land on every onset (hats,
        // stabs, vocals), but the bar starts where the kick energy concentrates, so the bar anchor must
        // come from the low band. Falls back to the beat anchor (no bar phase) when the band can't form.
        double[] kickEnvelope = _kickOnset.Compute(mono, sampleRate);
        DownbeatEstimate downbeat = kickEnvelope.Length > 0
            ? _downbeat.Estimate(kickEnvelope, bpm, _kickOnset.EnvelopeRateHz(sampleRate), firstBeatSeconds)
            : new DownbeatEstimate(firstBeatSeconds, 4, 0.0);

        return new BpmResult(bpm, Math.Round(estimate.Confidence, 4), firstBeatSeconds)
        {
            DownbeatSeconds = Math.Round(downbeat.DownbeatSeconds, 4),
            BeatsPerBar = downbeat.BeatsPerBar,
            DownbeatConfidence = downbeat.Confidence,
        };
    }
}
