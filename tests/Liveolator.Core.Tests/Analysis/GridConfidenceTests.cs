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
