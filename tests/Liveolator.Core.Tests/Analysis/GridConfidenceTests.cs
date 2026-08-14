using Liveolator.Core.Analysis.Bpm;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

/// <summary>
/// The grid-confidence gate (SYNC-BEHAVIOR-SPEC §7): fuse the persisted grid signals into an offer/downgrade
/// decision. Covers the weakest-link gate (a single bad signal downgrades), the conservative floor, and the
/// pre-v9 "unknown ⇒ preserve phase sync" back-compat path.
/// </summary>
public sealed class GridConfidenceTests
{
    [Fact]
    public void CleanGrid_StableTempo_OffersPhaseSync()
    {
        GridConfidence c = GridConfidenceCalculator.Evaluate(gridCoherence: 0.85, tempoStabilityBpmDelta: 0.0);

        Assert.True(c.PhaseSyncReady);
        Assert.True(c.Analyzed);
        Assert.NotNull(c.Display);
        Assert.True(c.Display > 0.9, $"a clean, stable grid should read near full quality, was {c.Display:F3}");
    }

    [Fact]
    public void LowCoherence_DowngradesToTempoOnly()
    {
        // A grid the kicks don't sit on: coherence below the phase-sync floor even though the tempo is steady.
        GridConfidence c = GridConfidenceCalculator.Evaluate(gridCoherence: 0.30, tempoStabilityBpmDelta: 0.0);

        Assert.False(c.PhaseSyncReady);
        Assert.True(c.Analyzed);
    }

    [Fact]
    public void VariableTempo_DowngradesToTempoOnly()
    {
        // A tight local fit but the tempo drifts 2 BPM across the track (live/acoustic) — phase would drift.
        GridConfidence c = GridConfidenceCalculator.Evaluate(gridCoherence: 0.90, tempoStabilityBpmDelta: 2.0);

        Assert.False(c.PhaseSyncReady);
    }

    [Fact]
    public void WeakestLink_OneBadSignalFails_EvenWhenTheOtherIsPerfect()
    {
        // Perfect coherence but drifting tempo must NOT pass — proves the gate is min(), not an average
        // (an average of {1.0, low} would clear the floor and wrongly offer phase sync).
        GridConfidence c = GridConfidenceCalculator.Evaluate(gridCoherence: 1.0, tempoStabilityBpmDelta: 1.5);

        Assert.False(c.PhaseSyncReady);
    }

    [Fact]
    public void AnalyzedButIncoherent_DowngradesButIsAnalyzed()
    {
        // An ambient / no-kick track: analyzed at v9 (coherence 0, non-null) — downgrade, but NOT "unknown".
        GridConfidence c = GridConfidenceCalculator.Evaluate(gridCoherence: 0.0, tempoStabilityBpmDelta: 0.0);

        Assert.False(c.PhaseSyncReady);
        Assert.True(c.Analyzed);
        Assert.Equal(0.0, c.Display);
    }

    [Fact]
    public void MissingSignals_AreUnknown_AndPreservePhaseSync()
    {
        // A pre-v9 catalog track has no grid signals: quality unknown, so phase sync is preserved (no
        // silent library-wide downgrade before the background re-analysis populates the signals).
        GridConfidence c = GridConfidenceCalculator.Evaluate(gridCoherence: null, tempoStabilityBpmDelta: null);

        Assert.True(c.PhaseSyncReady);
        Assert.False(c.Analyzed);
        Assert.Null(c.Display);
    }

    [Fact]
    public void OneMissingSignal_IsAlsoUnknown()
    {
        Assert.Equal(GridConfidence.Unknown, GridConfidenceCalculator.Evaluate(gridCoherence: 0.9, tempoStabilityBpmDelta: null));
        Assert.Equal(GridConfidence.Unknown, GridConfidenceCalculator.Evaluate(gridCoherence: null, tempoStabilityBpmDelta: 0.0));
    }

    [Fact]
    public void RefusedPhase_DowngradesPhaseSync_ButStillWarps()
    {
        // v12: the analyzer measured the phase and could not vouch for it (the low band is no louder at the
        // chosen phase than half a beat away). Phase sync must go, the warp must stay — treating a phase
        // refusal as a tempo refusal leaves the record at its native rate against the set tempo.
        GridConfidence c = GridConfidenceCalculator.Evaluate(
            gridCoherence: 0.9,
            tempoStabilityBpmDelta: 0.0,
            kickPhaseMarginRatio: 0.4,
            phaseWindowDisagreementSeconds: 0.002);

        Assert.False(c.PhaseSyncReady);
        Assert.True(c.TempoTrusted);
        Assert.True(c.Analyzed);
    }

    [Fact]
    public void PhaseThatMovesBetweenWindows_DowngradesPhaseSync()
    {
        GridConfidence c = GridConfidenceCalculator.Evaluate(
            gridCoherence: 0.9, tempoStabilityBpmDelta: 0.0,
            kickPhaseMarginRatio: 9.0, phaseWindowDisagreementSeconds: 0.167);

        Assert.False(c.PhaseSyncReady);
    }

    [Fact]
    public void VouchedPhase_OffersPhaseSync()
    {
        GridConfidence c = GridConfidenceCalculator.Evaluate(
            gridCoherence: 0.9, tempoStabilityBpmDelta: 0.0,
            kickPhaseMarginRatio: 6.0, phaseWindowDisagreementSeconds: 0.003);

        Assert.True(c.PhaseSyncReady);
    }

    [Fact]
    public void VouchedPhase_OffersPhaseSync_EvenWhenTheKickFitIsLoose()
    {
        // THE measured case (v12): coherence 0.371 is far under the phase floor, yet the kick-identity and
        // cross-window gates vouched for this anchor — "09 - Coming Soon - African Jungle", measured 6.4 ms
        // from an audio-derived reference. Coherence was proven uninformative about anchor correctness on
        // this material (spearman −0.555, and coherence 0.641 came with a 193.8 ms error), so it must not
        // veto a phase that has been measured right. The fit quality is still REPORTED honestly.
        GridConfidence c = GridConfidenceCalculator.Evaluate(
            gridCoherence: 0.371,
            tempoStabilityBpmDelta: 0.0,
            kickPhaseMarginRatio: 2.592,
            phaseWindowDisagreementSeconds: 0.0);

        Assert.True(c.PhaseSyncReady, "a directly verified anchor must not be vetoed by the grid fit");
        Assert.True(c.TempoTrusted);
        Assert.True(c.Display < GridConfidenceCalculator.PhaseSyncFloor, $"display was {c.Display:F3}");
    }

    [Fact]
    public void VouchedPhase_WithADriftingTempo_StillDowngrades()
    {
        // A phase measured right somewhere cannot be held over a record whose tempo moves 2 BPM across it:
        // there is no one phase to align to. Tempo stability stays a must-pass for phase — and it is the
        // same signal that gates the warp, so a phase lock is never offered on a clip that is not stretched.
        GridConfidence c = GridConfidenceCalculator.Evaluate(
            gridCoherence: 0.9, tempoStabilityBpmDelta: 2.0,
            kickPhaseMarginRatio: 6.0, phaseWindowDisagreementSeconds: 0.001);

        Assert.False(c.PhaseSyncReady);
        Assert.False(c.TempoTrusted);
    }

    [Fact]
    public void PreV12CatalogRow_Deserializes_AndKeepsItsPriorVerdict()
    {
        // A row written by v9-v11 has the two grid signals and no phase fields at all. It must still load
        // and be judged exactly as before (coherence + stability), not silently lose phase sync.
        BpmResult row = System.Text.Json.JsonSerializer.Deserialize<BpmResult>(
            """{"Bpm":145,"Confidence":0.9,"FirstBeatSeconds":0.03,"GridCoherence":0.9,"TempoStabilityBpmDelta":0.0}""")!;

        Assert.Null(row.KickPhaseMarginRatio);
        Assert.Null(row.PhaseWindowDisagreementSeconds);
        Assert.True(GridConfidenceCalculator.Evaluate(row).PhaseSyncReady);
        Assert.False(GridConfidenceCalculator
            .Evaluate(row with { GridCoherence = 0.3 }).PhaseSyncReady);
    }

    [Fact]
    public void PreV12Rows_HaveNoPhaseEvidence_AndKeepTheirVerdict()
    {
        // A v9-v11 catalog carries the two grid signals but neither phase signal. Absent evidence must not
        // read as a refusal, or the whole library silently loses phase sync before it re-analyzes.
        Assert.True(GridConfidenceCalculator
            .Evaluate(gridCoherence: 0.9, tempoStabilityBpmDelta: 0.0).PhaseSyncReady);
    }

    [Fact]
    public void EvaluatesTheRefusalFromTheBpmResult()
    {
        var refused = new BpmResult(145.0, 0.9)
        {
            GridCoherence = 0.9,
            TempoStabilityBpmDelta = 0.0,
            KickPhaseMarginRatio = 0.4,
            PhaseWindowDisagreementSeconds = 0.002,
        };

        Assert.False(GridConfidenceCalculator.Evaluate(refused).PhaseSyncReady);
    }

    [Fact]
    public void NullBpmResult_IsUnknown()
    {
        Assert.Equal(GridConfidence.Unknown, GridConfidenceCalculator.Evaluate((BpmResult?)null));
    }

    [Fact]
    public void EvaluatesFromBpmResult_UsingItsSignals()
    {
        var result = new BpmResult(128.0, 0.9) { GridCoherence = 0.85, TempoStabilityBpmDelta = 0.0 };

        Assert.True(GridConfidenceCalculator.Evaluate(result).PhaseSyncReady);
    }
}
