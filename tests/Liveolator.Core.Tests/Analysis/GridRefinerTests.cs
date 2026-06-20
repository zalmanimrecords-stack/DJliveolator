using System;
using Liveolator.Core.Analysis.Bpm;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

/// <summary>
/// Covers the sub-frame grid refinement: it must recover a clean tempo the integer-lag autocorrelation
/// cannot reach (the 139.67-vs-140 bug), recover the beat phase, reconcile octave/metrical confusions,
/// and degrade to the coarse estimate when there is no kick structure to fit.
/// </summary>
public sealed class GridRefinerTests
{
    private const double RateHz = 44100.0 / 512.0; // the kick-onset envelope rate (~86.13 Hz)

    [Fact]
    public void Refine_RecoversExactTempo_TheIntegerLagCannotReach()
    {
        // True 140.0; the coarse autocorrelation lands on 139.67 (nearest integer lag). The refiner must
        // pull it back to a clean 140 and the grid must stay on the kicks across a long track.
        double[] env = KickEnvelope(trueBpm: 140.0, seconds: 360, firstBeatSeconds: 0.0);

        GridFit fit = new GridRefiner().Refine(env, RateHz, coarseBpm: 139.67, coarseFirstBeatSeconds: 0.0);

        Assert.Equal(140.0, fit.Bpm, 1);                 // within 0.05 BPM
        Assert.True(fit.Coherence > 0.9, $"clean kicks should fit tightly, was {fit.Coherence:F3}");
    }

    [Fact]
    public void Refine_RecoversTheBeatPhase()
    {
        const double offset = 0.137;
        double[] env = KickEnvelope(trueBpm: 140.0, seconds: 120, firstBeatSeconds: offset);

        GridFit fit = new GridRefiner().Refine(env, RateHz, coarseBpm: 139.67, coarseFirstBeatSeconds: 0.0);

        double period = 60.0 / fit.Bpm;
        // The phase is modulo one beat; compare on the circle so 0 and ~period are treated as equal.
        double diff = Math.Abs(fit.FirstBeatSeconds - offset) % period;
        double circular = Math.Min(diff, period - diff);
        Assert.True(circular < 0.02, $"first-beat off by {circular:F3}s (got {fit.FirstBeatSeconds:F3}, want {offset})");
    }

    [Fact]
    public void Refine_ReconcilesMetricalConfusion_FromTwoThirdsOctaveError()
    {
        // Kicks are really at 138.0; a coarse estimate slipped to 92.0 (≈ 138 × 2/3). The 3:2 candidate
        // lands the grid back on the real kicks, inside the target band.
        double[] env = KickEnvelope(trueBpm: 138.0, seconds: 180, firstBeatSeconds: 0.0);

        GridFit fit = new GridRefiner().Refine(env, RateHz, coarseBpm: 92.0, coarseFirstBeatSeconds: 0.0);

        Assert.InRange(fit.Bpm, 137.5, 138.5);
        Assert.True(fit.Coherence > 0.9, $"the correct meter should fit tightly, was {fit.Coherence:F3}");
    }

    [Fact]
    public void Refine_NoKickStructure_ReturnsLowCoherence_SoTheCallerFallsBack()
    {
        var rng = new Random(1);
        var env = new double[20000];
        for (int i = 0; i < env.Length; i++)
            env[i] = rng.NextDouble() * 0.01; // noise floor, no periodic kicks

        GridFit fit = new GridRefiner().Refine(env, RateHz, coarseBpm: 128.0, coarseFirstBeatSeconds: 0.0);

        Assert.True(fit.Coherence < GridRefiner.AcceptCoherence,
            $"random noise must not read as a confident grid, was {fit.Coherence:F3}");
    }

    [Fact]
    public void Refine_EmptyEnvelope_ReturnsCoarseUnchanged()
    {
        GridFit fit = new GridRefiner().Refine(Array.Empty<double>(), RateHz, 128.0, 0.05);

        Assert.Equal(128.0, fit.Bpm, 6);
        Assert.Equal(0.05, fit.FirstBeatSeconds, 6);
        Assert.Equal(0.0, fit.Coherence, 6);
    }

    // A kick-onset envelope: one unit spike per beat at the true tempo (everything else silent), the shape
    // GridRefiner fits against. Frame index = round(beatTime * rate).
    private static double[] KickEnvelope(double trueBpm, double seconds, double firstBeatSeconds)
    {
        int frames = (int)(seconds * RateHz);
        var env = new double[frames];
        double beat = 60.0 / trueBpm;
        for (double t = firstBeatSeconds; t < seconds; t += beat)
        {
            int f = (int)Math.Round(t * RateHz);
            if (f >= 0 && f < frames)
                env[f] = 1.0;
        }
        return env;
    }
}
