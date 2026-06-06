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
public sealed record BpmResult(double Bpm, double Confidence, double FirstBeatSeconds = 0.0);

/// <summary>
/// Orchestrates the BPM pipeline: mono PCM → onset envelope → tempo estimate → first-beat anchor.
/// Pure and hardware-free; the decode that produces the PCM lives behind <c>IAudioDecoder</c>.
/// </summary>
public sealed class BpmDetector
{
    private readonly OnsetEnvelope _onset;
    private readonly TempoEstimator _tempo;
    private readonly FirstBeatEstimator _firstBeat;

    public BpmDetector(
        OnsetEnvelope? onset = null, TempoEstimator? tempo = null, FirstBeatEstimator? firstBeat = null)
    {
        _onset = onset ?? new OnsetEnvelope();
        _tempo = tempo ?? new TempoEstimator();
        _firstBeat = firstBeat ?? new FirstBeatEstimator();
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
        return new BpmResult(bpm, Math.Round(estimate.Confidence, 4), firstBeatSeconds);
    }
}
