using Liveolator.Core.Analysis.Bpm;
using Xunit;

namespace Liveolator.Core.Tests.Analysis.Bpm;

/// <summary>
/// The two gates that decide whether a measured beat phase may be published as a track's anchor.
/// <para>Both exist because the estimator's own confidence is uninformative about the one error that
/// matters: on the measured 11-track set spearman(confidence, phase error) = −0.164, the
/// highest-confidence track (0.740) was 180.9 ms wrong, and a 0.405 track was right to 10.1 ms. A
/// half-beat error is precisely the case where a tight, populous onset cluster exists at the WRONG
/// place, so it scores high by construction. Grid coherence is no better (−0.555; coherence 0.641 →
/// 193.8 ms error). What DOES separate right from wrong is (1) whether the low band is actually louder
/// at the chosen phase than at its antiphase, and (2) whether one phase fits the whole file.</para>
/// </summary>
public sealed class KickPhaseGateTests
{
    private const double Bpm = 145.0;
    private const double Period = 60.0 / Bpm;
    private const double FrameRateHz = 86.13; // BandEnergyEnvelope framing at 44.1 kHz / hop 512

    [Fact]
    public void BeatProfile_FoldsARepeatingHumpBackOntoItsPhase()
    {
        double[] frames = LowBandFrames(kickPhaseSeconds: 0.10, seconds: 120.0);

        double[] profile = KickPhaseGate.BeatProfile(frames, FrameRateHz, Bpm);

        Assert.NotEmpty(profile);
        int peak = Array.IndexOf(profile, profile.Max());
        double peakSeconds = (peak + 0.5) / profile.Length * Period;
        Assert.True(
            Math.Abs(peakSeconds - 0.10) < 0.02,
            $"the folded profile should peak at the hump's phase (0.100 s); peaked at {peakSeconds:F3} s");
    }

    [Fact]
    public void MarginRatio_ClearsTheGateOnTheKickAndFailsAtItsAntiphase()
    {
        double[] profile = KickPhaseGate.BeatProfile(
            LowBandFrames(kickPhaseSeconds: 0.10, seconds: 120.0), FrameRateHz, Bpm);

        double onKick = KickPhaseGate.MarginRatio(profile, Bpm, 0.10);
        double onAntiphase = KickPhaseGate.MarginRatio(profile, Bpm, 0.10 + (Period / 2.0));

        Assert.True(onKick >= KickPhaseGate.MinimumMarginRatio, $"kick margin {onKick:F2}");
        Assert.True(onAntiphase < KickPhaseGate.MinimumMarginRatio, $"antiphase margin {onAntiphase:F2}");
        // The whole point of the gate: an antiphase win is refused rather than published.
        Assert.True(KickPhaseGate.Passes(onKick, windowDisagreementSeconds: 0.001));
        Assert.False(KickPhaseGate.Passes(onAntiphase, windowDisagreementSeconds: 0.001));
    }

    [Fact]
    public void MarginRatio_FlatLowBand_HasNoKickToIdentify()
    {
        // Ambient / no-kick material: the fold has no hump, so no phase can be vouched for — the ratio
        // must not read ~1.0 by luck and squeak past the floor.
        var flat = new double[64];
        Array.Fill(flat, 3.0);

        Assert.Equal(0.0, KickPhaseGate.MarginRatio(flat, Bpm, 0.10));
    }

    [Fact]
    public void Passes_NeedsBothSignals_SoMissingEvidenceIsNeverAPass()
    {
        Assert.False(KickPhaseGate.Passes(marginRatio: null, windowDisagreementSeconds: null));
        Assert.False(KickPhaseGate.Passes(marginRatio: 9.0, windowDisagreementSeconds: null));
        Assert.False(KickPhaseGate.Passes(marginRatio: null, windowDisagreementSeconds: 0.0));
    }

    [Fact]
    public void Passes_RejectsAStrongMarginWhenTheWindowsDisagree()
    {
        // The measured refusal case ("Vibe Tribe and Spade - Beyond and Beyond"): the declared 145.09 BPM
        // is itself wrong, so no single global phase exists — mid-file and whole-file disagreed by 167 ms
        // where ten other tracks agreed to 0.1-5.9 ms.
        Assert.False(KickPhaseGate.Passes(marginRatio: 9.0, windowDisagreementSeconds: 0.167));
        Assert.True(KickPhaseGate.Passes(marginRatio: 9.0, windowDisagreementSeconds: 0.006));
    }

    [Fact]
    public void WindowDisagreement_IsNearZeroWhenOnePhaseFitsTheWholeFile()
    {
        IReadOnlyList<double> onsets = Onsets(seconds: 200.0, beatSeconds: Period, phase: 0.10);

        double? disagreement = KickPhaseGate.WindowDisagreementSeconds(onsets, Bpm, 0.10);

        Assert.NotNull(disagreement);
        Assert.True(disagreement <= KickPhaseGate.MaximumWindowDisagreementSeconds, $"{disagreement:F4} s");
    }

    [Fact]
    public void WindowDisagreement_ExposesATrackWithNoSingleGlobalPhase()
    {
        // The kicks land 0.1% apart from the declared tempo, so the phase walks through the file: a fit
        // over one window says one thing, a fit over the whole file another. That is the shape of a track
        // whose declared BPM is wrong, and it must be refused rather than aligned on.
        IReadOnlyList<double> drifting = Onsets(seconds: 200.0, beatSeconds: Period * 1.001, phase: 0.10);
        Assert.True(drifting.Count > 400);

        Core.Analysis.Bpm.KickPhase whole = KickPhaseEstimator.Estimate(drifting, Bpm);
        double? disagreement = KickPhaseGate.WindowDisagreementSeconds(drifting, Bpm, whole.PhaseSeconds);

        Assert.NotNull(disagreement);
        Assert.True(
            disagreement > KickPhaseGate.MaximumWindowDisagreementSeconds,
            $"a drifting phase must not read as stable; disagreement {disagreement:F4} s");
    }

    [Fact]
    public void WindowDisagreement_TooFewOnsetsIsNoEvidence()
        => Assert.Null(KickPhaseGate.WindowDisagreementSeconds(new[] { 0.1, 0.5, 0.9 }, Bpm, 0.1));

    // A low-band amplitude frame series: a broad kick hump on every beat over a quiet floor, which is
    // what BandEnergyEnvelope.Low looks like on four-on-the-floor.
    private static double[] LowBandFrames(double kickPhaseSeconds, double seconds)
    {
        var frames = new double[(int)(FrameRateHz * seconds)];
        for (int f = 0; f < frames.Length; f++)
        {
            double distance = CircularDistance(f / FrameRateHz, kickPhaseSeconds);
            frames[f] = 1.0 + (6.0 * Math.Exp(-0.5 * distance * distance / (0.02 * 0.02)));
        }

        return frames;
    }

    private static double[] Onsets(double seconds, double beatSeconds, double phase)
    {
        var onsets = new List<double>();
        for (double t = phase; t < seconds; t += beatSeconds)
            onsets.Add(t);
        return onsets.ToArray();
    }

    private static double CircularDistance(double t, double phase)
    {
        double d = Math.Abs((((t - phase) % Period) + Period) % Period);
        return Math.Min(d, Period - d);
    }
}
