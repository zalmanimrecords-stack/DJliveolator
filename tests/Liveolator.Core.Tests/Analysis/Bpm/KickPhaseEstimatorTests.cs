using Liveolator.Core.Analysis.Bpm;

namespace Liveolator.Core.Tests.Analysis.Bpm;

/// <summary>
/// The phase estimator that fixes beat matching. The case that matters is the NOISY one: on the real set
/// the stored onset list had a circular concentration of 0.043 — all but uniform — so an estimator that
/// only works on clean input is useless here.
/// <para>At 140 BPM a beat is 428.571 ms.</para>
/// </summary>
public sealed class KickPhaseEstimatorTests
{
    private const double Bpm = 140.0;
    private const double Period = 60.0 / Bpm;

    /// <summary>Onsets exactly on <paramref name="phase"/>, one per beat.</summary>
    private static List<double> OnBeat(double phase, int count, double bpm = Bpm)
    {
        double period = 60.0 / bpm;
        var list = new List<double>(count);
        for (int i = 0; i < count; i++)
            list.Add(phase + (i * period));
        return list;
    }

    /// <summary>Deterministic pseudo-noise spread across the beat, standing in for non-kick transients.</summary>
    private static IEnumerable<double> Junk(int count, double period, int seed = 7)
    {
        // A prime-step walk around the circle: uniform-ish, and identical on every run.
        double step = period * 0.3819660112501051;   // golden-ratio conjugate, so it never repeats early
        double at = period * 0.11;
        for (int i = 0; i < count; i++)
        {
            at = (at + step) % period;
            yield return at + (i * period * (seed % 3 + 1));
        }
    }

    [Fact]
    public void Estimate_FindsThePhase_OfCleanOnBeatOnsets()
    {
        KickPhase phase = KickPhaseEstimator.Estimate(OnBeat(0.100, 40), Bpm);

        Assert.Equal(0.100, phase.PhaseSeconds, precision: 6);
        Assert.Equal(1.0, phase.Confidence, precision: 6);
        Assert.Equal(40, phase.Inliers);
    }

    [Fact]
    public void Estimate_FindsThePhase_BuriedInMostlyJunk()
    {
        // THE case: 30 real kicks against 90 spurious onsets, i.e. only a quarter of the list is signal.
        var onsets = OnBeat(0.100, 30);
        onsets.AddRange(Junk(90, Period));

        KickPhase phase = KickPhaseEstimator.Estimate(onsets, Bpm);

        Assert.InRange(phase.PhaseSeconds, 0.100 - 0.010, 0.100 + 0.010);
        Assert.True(phase.Confidence > 0.1, $"confidence too low to act on: {phase.Confidence}");
    }

    [Fact]
    public void Estimate_AbsorbsDetectorJitter_OnRealKicks()
    {
        // Kicks detected a few ms early/late around a true 0.150 s phase.
        var onsets = new List<double>();
        double[] jitter = { 0.004, -0.006, 0.002, -0.003, 0.007, -0.001, 0.005, -0.004, 0.000, 0.003 };
        for (int i = 0; i < 30; i++)
            onsets.Add(0.150 + jitter[i % jitter.Length] + (i * Period));

        KickPhase phase = KickPhaseEstimator.Estimate(onsets, Bpm);

        Assert.InRange(phase.PhaseSeconds, 0.150 - 0.004, 0.150 + 0.004);
    }

    [Fact]
    public void Estimate_ReportsNoConfidence_ForStructurelessOnsets()
    {
        // Uniform onsets must not yield a phase the caller would trust — refusing beats guessing, which is
        // the whole reason the old anchor was wrong.
        KickPhase phase = KickPhaseEstimator.Estimate(Junk(200, Period).ToList(), Bpm);

        Assert.True(phase.Confidence < 0.2, $"structureless onsets reported confidence {phase.Confidence}");
    }

    [Fact]
    public void Estimate_HandlesPhaseNearTheWrapPoint()
    {
        // A phase just under the period must not be split across the 0 boundary and averaged to the middle.
        double nearEnd = Period - 0.008;
        var onsets = new List<double>();
        double[] jitter = { 0.003, -0.004, 0.001, -0.002 };
        for (int i = 0; i < 24; i++)
            onsets.Add(nearEnd + jitter[i % jitter.Length] + (i * Period));

        KickPhase phase = KickPhaseEstimator.Estimate(onsets, Bpm);

        double offset = Math.Abs(((phase.PhaseSeconds - nearEnd + (Period * 1.5)) % Period) - (Period / 2));
        Assert.True(offset < 0.005, $"wrapped phase went to {phase.PhaseSeconds}, expected ~{nearEnd}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void Estimate_ReturnsNone_WhenThereIsTooLittleToGoOn(int count)
    {
        Assert.Equal(KickPhase.None, KickPhaseEstimator.Estimate(OnBeat(0.1, count), Bpm));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-140.0)]
    public void Estimate_ReturnsNone_ForUnusableInput(double? bpm)
    {
        Assert.Equal(KickPhase.None, KickPhaseEstimator.Estimate(bpm is null ? null : OnBeat(0.1, 40), bpm ?? Bpm));
    }

    [Fact]
    public void Estimate_Rejects_AToleranceThatWouldMatchEverything()
    {
        // At half a beat every onset agrees with every hypothesis, so the result would be meaningless.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => KickPhaseEstimator.Estimate(OnBeat(0.1, 40), Bpm, toleranceSeconds: Period / 2));
    }

    [Fact]
    public void Estimate_AlwaysReturnsAPhase_InsideOneBeat()
    {
        KickPhase phase = KickPhaseEstimator.Estimate(OnBeat(Period * 0.97, 40), Bpm);

        Assert.InRange(phase.PhaseSeconds, 0.0, Period);
    }

    [Fact]
    public void SnapToPhase_MovesAnAnchor_OntoTheBeat()
    {
        // The anchor error measured on the real set was ~0.165 s; snapping must remove it.
        double wrongAnchor = 0.100 + 0.165;

        double snapped = KickPhaseEstimator.SnapToPhase(wrongAnchor, 0.100, Bpm);

        Assert.Equal(0.100, snapped % Period, precision: 6);
    }

    [Fact]
    public void SnapToPhase_NeverMovesByMoreThanHalfABeat()
    {
        for (int i = 0; i <= 40; i++)
        {
            double t = 3.0 + (i * Period / 40.0);
            double snapped = KickPhaseEstimator.SnapToPhase(t, 0.100, Bpm);
            Assert.True(
                Math.Abs(snapped - t) <= (Period / 2) + 1e-9,
                $"moved {Math.Abs(snapped - t):F4}s from {t:F4}, more than half a beat");
        }
    }

    [Fact]
    public void SnapToPhase_NeverReturnsANegativeTime()
    {
        Assert.True(KickPhaseEstimator.SnapToPhase(0.01, Period - 0.01, Bpm) >= 0.0);
    }
}
