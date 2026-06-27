namespace Liveolator.Core.Analysis.Bpm;

/// <summary>
/// A kick-focused onset-detection envelope: one value per analysis frame, large where a kick (the beat
/// anchor) strikes and small elsewhere. Implementations differ in how aggressively they reject non-kick
/// low-frequency energy (a sustained bassline / sub / 808 that lands in the same band): the simple band
/// split (<see cref="LowBandOnsetEnvelope"/>) cannot, percussive separation
/// (<see cref="PercussiveOnsetEnvelope"/>) can. The offline BPM pipeline (<see cref="BpmDetector"/>) reads
/// this through the seam so the separation strategy can change without touching the tempo/phase stages.
/// Pure and hardware-free (doc 16); offline analysis only.
/// </summary>
public interface IKickOnsetEnvelope
{
    /// <summary>The kick-onset envelope (one value per analysis frame); empty when the band cannot form.</summary>
    double[] Compute(ReadOnlySpan<float> mono, int sampleRate);

    /// <summary>Envelope frames per second for a given audio sample rate.</summary>
    double EnvelopeRateHz(int sampleRate);

    /// <summary>
    /// Seconds to ADD back to a time derived from this envelope so it lands on the true onset (the
    /// analysis frame is timestamped at its start but represents audio centred ~half a frame later).
    /// </summary>
    double AnalysisLatencySeconds(int sampleRate);
}
