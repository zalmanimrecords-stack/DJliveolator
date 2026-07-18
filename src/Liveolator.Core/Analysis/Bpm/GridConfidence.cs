namespace Liveolator.Core.Analysis.Bpm;

/// <summary>
/// The grid-quality verdict for a track: a 0..1 display confidence and the gate decision Sync uses to
/// choose beat/phase sync vs. a tempo-only downgrade (spec: SYNC-BEHAVIOR-SPEC §7). Sync is only as good
/// as the beatgrid, so a low-confidence grid must NOT phase-align (a confident-but-wrong lock that drifts
/// on a full floor costs far more trust than an unnecessary tempo-only downgrade).
/// </summary>
/// <param name="Display">0..1 grid quality for the UI (weighted product of the signals); null when the
/// track predates grid-confidence analysis (an older catalog), so the UI shows nothing rather than 0.</param>
/// <param name="PhaseSyncReady">True ⇒ offer beat/phase sync; false ⇒ downgrade to tempo-only.</param>
/// <param name="Analyzed">False when the signals are absent (pre-v9 catalog) — <see cref="PhaseSyncReady"/>
/// then preserves the prior behaviour (allow phase sync) until the track re-analyzes.</param>
public readonly record struct GridConfidence(double? Display, bool PhaseSyncReady, bool Analyzed)
{
    /// <summary>A track analyzed before grid-confidence existed: quality unknown, phase sync preserved.</summary>
    public static GridConfidence Unknown { get; } = new(Display: null, PhaseSyncReady: true, Analyzed: false);
}

/// <summary>
/// Fuses the analyzed grid signals into a <see cref="GridConfidence"/>. Pure and hardware-free (unit-tests
/// under xUnit). Design decisions (SYNC-BEHAVIOR-SPEC §7, owner-approved 2026-07-17):
/// <list type="bullet">
/// <item><b>Weakest-link gate.</b> A confident phase-lock genuinely needs BOTH a tight grid fit AND a
/// stable tempo — so the gate is the <em>minimum</em> of the must-pass signals, not an average (an average
/// lets one strong signal mask a fatal weak one).</item>
/// <item><b>Conservative, asymmetric floor.</b> 0.6 — the calibrated equivalent of the Essentia/Zapata
/// "good ≈ 1.5-bit" beat-tracking line — biased toward downgrade.</item>
/// <item><b>Downbeat is deliberately NOT in the beat-level gate.</b> Four-on-the-floor has low downbeat
/// confidence yet is the most syncable material; bar-level alignment gates on the downbeat separately in
/// the engine, so requiring it here would wrongly block beat-level phase sync on exactly the easy cases.</item>
/// </list>
/// The normalization constants are corpus-calibration knobs — the raw signals are what get persisted, so
/// these can be retuned without re-analyzing the catalog.
/// </summary>
public static class GridConfidenceCalculator
{
    /// <summary>Coherence at/below which the kick fit is untrustworthy — the <see cref="GridRefiner"/> floor.</summary>
    public const double CoherenceFloor = GridRefiner.AcceptCoherence; // 0.15

    /// <summary>Coherence at/above which the grid fit is treated as "clean" (maps to 1.0). Clean electronic
    /// kicks fit &gt; 0.9; this reference keeps the 0..1 mapping meaningful for real material.</summary>
    public const double CoherenceReference = 0.85;

    /// <summary>Gaussian width (BPM) for the constant-tempo signal: a half-vs-half tempo delta this large
    /// scores ≈0.61. Wide enough that detection noise on a genuinely constant track does not read as drift,
    /// tight enough that a variable-tempo (live/acoustic) track fails.</summary>
    public const double StabilitySigmaBpm = 0.75;

    /// <summary>Each must-pass signal must reach this to offer phase sync (the weakest-link floor).</summary>
    public const double PhaseSyncFloor = 0.6;

    /// <summary>Evaluate from a track's BPM result. Null result / missing signals ⇒ <see cref="GridConfidence.Unknown"/>.</summary>
    public static GridConfidence Evaluate(BpmResult? result)
        => result is null
            ? GridConfidence.Unknown
            : Evaluate(result.GridCoherence, result.TempoStabilityBpmDelta);

    /// <summary>Evaluate from the raw persisted signals. Either signal absent ⇒ pre-v9 catalog ⇒ Unknown.</summary>
    public static GridConfidence Evaluate(double? gridCoherence, double? tempoStabilityBpmDelta)
    {
        if (gridCoherence is not double coherence || tempoStabilityBpmDelta is not double bpmDelta)
            return GridConfidence.Unknown;

        double coherenceN = NormalizeCoherence(coherence);
        double stabilityN = NormalizeStability(bpmDelta);
        bool ready = coherenceN >= PhaseSyncFloor && stabilityN >= PhaseSyncFloor;
        return new GridConfidence(Display: coherenceN * stabilityN, PhaseSyncReady: ready, Analyzed: true);
    }

    private static double NormalizeCoherence(double coherence)
        => Math.Clamp((coherence - CoherenceFloor) / (CoherenceReference - CoherenceFloor), 0.0, 1.0);

    private static double NormalizeStability(double bpmDelta)
    {
        double z = Math.Abs(bpmDelta) / StabilitySigmaBpm;
        return Math.Exp(-0.5 * z * z);
    }
}
