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
/// <param name="TempoTrusted">True ⇒ the detected tempo is stable enough to warp by. Deliberately separate
/// from <see cref="PhaseSyncReady"/>: the two signals answer different questions, and a smeared kick (weak
/// grid fit) says nothing about whether the tempo is constant. Consumers that stretch audio must gate on
/// this, not on <see cref="PhaseSyncReady"/> — treating a phase downgrade as a tempo downgrade leaves the
/// track at its native rate against the set tempo, which guarantees the drift the gate exists to prevent.</param>
public readonly record struct GridConfidence(
    double? Display,
    bool PhaseSyncReady,
    bool Analyzed,
    bool TempoTrusted = true)
{
    /// <summary>A track analyzed before grid-confidence existed: quality unknown, phase sync preserved.</summary>
    public static GridConfidence Unknown { get; } =
        new(Display: null, PhaseSyncReady: true, Analyzed: false, TempoTrusted: true);
}

/// <summary>
/// Fuses the analyzed grid signals into a <see cref="GridConfidence"/>. Pure and hardware-free (unit-tests
/// under xUnit). Design decisions (SYNC-BEHAVIOR-SPEC §7, owner-approved 2026-07-17):
/// <list type="bullet">
/// <item><b>Weakest-link gate.</b> A confident phase-lock needs the anchor to be right AND the tempo to be
/// constant — so the gate is the <em>minimum</em> of the must-pass signals, not an average (an average lets
/// one strong signal mask a fatal weak one). Since v12 the anchor term is the measured
/// <see cref="KickPhaseGate"/> verdict where it exists, and the grid fit only stands in for it on tracks
/// analyzed before that measurement (see <see cref="Evaluate(double?, double?, double?, double?)"/>).</item>
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
            : Evaluate(
                result.GridCoherence,
                result.TempoStabilityBpmDelta,
                result.KickPhaseMarginRatio,
                result.PhaseWindowDisagreementSeconds);

    /// <summary>
    /// Evaluate from the raw persisted signals. Either grid signal absent ⇒ pre-v9 catalog ⇒ Unknown.
    /// <para><b>The phase signals (v12) DECIDE phase readiness whenever they exist</b>, and
    /// <see cref="BpmResult.GridCoherence"/> only decides it for a track that has none. The
    /// <see cref="KickPhaseGate"/> measures the anchor itself — the low band louder at the anchor than half a
    /// beat away, and the same phase found again over a second window — whereas coherence measures how
    /// tightly onsets sit on the grid, which was proven uninformative about whether the anchor is the KICK
    /// (spearman −0.555 over the measured set, with coherence 0.641 carrying a 193.8 ms error, so no usable
    /// floor exists). Letting the proxy veto the direct measurement cost four of eleven tracks their phase
    /// lock and the long blend while their anchors were right to 6.4-15.8 ms.</para>
    /// <para>Tempo stability stays a must-pass for phase either way: over a record whose tempo moves there is
    /// no single phase to align to, and it is the signal the warp is gated on, so a phase lock is never
    /// offered for a clip that will not be stretched.</para>
    /// </summary>
    public static GridConfidence Evaluate(
        double? gridCoherence,
        double? tempoStabilityBpmDelta,
        double? kickPhaseMarginRatio = null,
        double? phaseWindowDisagreementSeconds = null)
    {
        if (gridCoherence is not double coherence || tempoStabilityBpmDelta is not double bpmDelta)
            return GridConfidence.Unknown;

        bool phaseMeasured = kickPhaseMarginRatio is not null || phaseWindowDisagreementSeconds is not null;
        double coherenceN = NormalizeCoherence(coherence);
        double stabilityN = NormalizeStability(bpmDelta);
        bool anchorTrusted = phaseMeasured
            ? KickPhaseGate.Passes(kickPhaseMarginRatio, phaseWindowDisagreementSeconds)
            : coherenceN >= PhaseSyncFloor;
        bool ready = anchorTrusted && stabilityN >= PhaseSyncFloor;
        // Warping needs only a constant tempo; aligning phase additionally needs a trustworthy anchor. Gating
        // the stretch on the fused verdict would refuse to tempo-match a rock-steady record whose anchor is
        // merely unproven, which is the one case where stretching is unambiguously right.
        return new GridConfidence(
            Display: coherenceN * stabilityN,
            PhaseSyncReady: ready,
            Analyzed: true,
            TempoTrusted: stabilityN >= PhaseSyncFloor);
    }

    private static double NormalizeCoherence(double coherence)
        => Math.Clamp((coherence - CoherenceFloor) / (CoherenceReference - CoherenceFloor), 0.0, 1.0);

    private static double NormalizeStability(double bpmDelta)
    {
        double z = Math.Abs(bpmDelta) / StabilitySigmaBpm;
        return Math.Exp(-0.5 * z * z);
    }
}
