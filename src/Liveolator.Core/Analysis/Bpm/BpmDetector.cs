namespace Liveolator.Core.Analysis.Bpm;

/// <summary>Final BPM measurement for a track.</summary>
public sealed record BpmResult(double Bpm, double Confidence);

/// <summary>
/// Orchestrates the BPM pipeline: mono PCM → onset envelope → tempo estimate. Pure and
/// hardware-free; the decode that produces the PCM lives behind <c>IAudioDecoder</c>.
/// </summary>
public sealed class BpmDetector
{
    private readonly OnsetEnvelope _onset;
    private readonly TempoEstimator _tempo;

    public BpmDetector(OnsetEnvelope? onset = null, TempoEstimator? tempo = null)
    {
        _onset = onset ?? new OnsetEnvelope();
        _tempo = tempo ?? new TempoEstimator();
    }

    public BpmResult Detect(ReadOnlySpan<float> mono, int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        double[] envelope = _onset.Compute(mono);
        if (envelope.Length == 0)
            return new BpmResult(0, 0);

        TempoEstimate estimate = _tempo.Estimate(envelope, _onset.EnvelopeRateHz(sampleRate));
        return new BpmResult(Math.Round(estimate.Bpm, 2), Math.Round(estimate.Confidence, 4));
    }
}
