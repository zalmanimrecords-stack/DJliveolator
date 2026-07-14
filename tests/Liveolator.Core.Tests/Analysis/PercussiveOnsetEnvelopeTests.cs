using Liveolator.Core.Analysis.Bpm;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

/// <summary>
/// The HPSS (harmonic/percussive) kick-onset envelope. Its reason to exist is rejecting a sustained
/// in-band bass that the plain band split conflates with the kick, so the central test pits the two
/// against the same polluted signal and asserts the percussive one yields a tighter beat grid.
/// </summary>
public sealed class PercussiveOnsetEnvelopeTests
{
    private const int SampleRate = 44_100;

    [Fact]
    public void Compute_SustainedInBandBass_GivesTighterGridThanBandOnly()
    {
        // Kick on the beat + a sustained 110 Hz bass on the off-beat: both land under the 200 Hz crossover.
        float[] signal = BeatMixSignals.KickBassHatsFourOnFloor(
            128.0, SampleRate, seconds: 16.0, kickOffsetSeconds: 0.0, bassHz: 110.0);

        double coarseBpm = new TempoEstimator()
            .Estimate(new OnsetEnvelope().Compute(signal), new OnsetEnvelope().EnvelopeRateHz(SampleRate)).Bpm;

        GridFit band = RefineWith(new LowBandOnsetEnvelope(), signal, coarseBpm);
        GridFit percussive = RefineWith(new PercussiveOnsetEnvelope(), signal, coarseBpm);

        Assert.True(
            percussive.Coherence > band.Coherence,
            $"percussive grid should be tighter: percussive={percussive.Coherence:F3}, band={band.Coherence:F3}");
        Assert.True(
            percussive.Coherence >= GridRefiner.AcceptCoherence,
            $"percussive coherence {percussive.Coherence:F3} should clear the trust floor {GridRefiner.AcceptCoherence}");
    }

    [Fact]
    public void Compute_CleanKickTrain_RecoversTempo()
    {
        float[] kick = BeatMixSignals.KickBassHatsFourOnFloor(
            124.0, SampleRate, seconds: 12.0, bassHz: 320.0); // bass out of band: a clean kick case

        var detector = new PercussiveOnsetEnvelope();
        double[] envelope = detector.Compute(kick, SampleRate);
        TempoEstimate estimate = new TempoEstimator().Estimate(envelope, detector.EnvelopeRateHz(SampleRate));

        Assert.InRange(estimate.Bpm, 121.0, 127.0);
    }

    [Fact]
    public void Compute_TooShortSignal_ReturnsEmpty()
    {
        Assert.Empty(new PercussiveOnsetEnvelope().Compute(new float[16], SampleRate));
    }

    [Fact]
    public void Compute_SampleRateBelowCrossover_ReturnsEmpty()
    {
        float[] signal = BeatMixSignals.KickBassHatsFourOnFloor(120.0, sampleRate: 300, seconds: 4.0);
        Assert.Empty(new PercussiveOnsetEnvelope().Compute(signal, 300));
    }

    private static GridFit RefineWith(IKickOnsetEnvelope envelope, float[] signal, double coarseBpm)
    {
        double[] env = envelope.Compute(signal, SampleRate);
        return new GridRefiner().Refine(env, envelope.EnvelopeRateHz(SampleRate), coarseBpm, 0.0);
    }
}
