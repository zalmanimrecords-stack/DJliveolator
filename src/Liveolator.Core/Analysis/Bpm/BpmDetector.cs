using Liveolator.Core.Analysis.Cues;

namespace Liveolator.Core.Analysis.Bpm;

/// <summary>Final BPM measurement for a track.</summary>
/// <param name="Bpm">Detected tempo in BPM (0 when undetectable).</param>
/// <param name="Confidence">Detection confidence, 0..1.</param>
/// <param name="FirstBeatSeconds">
/// The first-beat (downbeat) anchor: the within-beat offset in seconds, in [0, 60/Bpm), where the beat
/// grid starts. Quantize/phase-match aligns decks against it (doc 11). 0 when tempo is undetectable.
/// Added after the original (Bpm, Confidence) shape with a default of 0 so existing consumers and
/// serialized catalogs are unaffected (positional-record back-compat).
/// </param>
public sealed record BpmResult(double Bpm, double Confidence, double FirstBeatSeconds = 0.0)
{
    /// <summary>
    /// The downbeat (beat 1) anchor: offset in seconds from track start to the first downbeat. Where
    /// <see cref="FirstBeatSeconds"/> fixes where beats land, this fixes where the <em>bar</em> starts,
    /// so bar/phrase-level alignment composes. 0 when undetectable. Init-only with a default so the
    /// positional (Bpm, Confidence, FirstBeatSeconds) shape and existing serialized catalogs are
    /// unaffected (positional-record back-compat).
    /// </summary>
    public double DownbeatSeconds { get; init; }

    /// <summary>The assumed meter; 4 for 4/4. Defaults to 4 for back-compat with pre-downbeat catalogs.</summary>
    public int BeatsPerBar { get; init; } = 4;

    /// <summary>
    /// 0..1 confidence in the downbeat placement. Low on four-on-the-floor (genuinely ambiguous at the
    /// bar level); consumers gate bar-level alignment on it rather than trusting a guessed downbeat.
    /// </summary>
    public double DownbeatConfidence { get; init; }

    /// <summary>
    /// Detected kick strike times in seconds (<see cref="KickOnsetPicker"/>), so a deck can snap its grid
    /// onto the real kick nearest the playhead (SET PHASE / one-shot SYNC auto-align) rather than a global
    /// anchor. Empty when the kick band could not form. Init-only with a default so the positional record
    /// shape and existing serialized catalogs are unaffected (positional-record back-compat).
    /// </summary>
    public IReadOnlyList<double> KickOnsetsSeconds { get; init; } = Array.Empty<double>();

    /// <summary>
    /// Raw onset-phase coherence (0..1) of the kick-grid fit (<see cref="GridRefiner"/>) — how tightly the
    /// kicks land on the grid, the primary predictor of a trustworthy constant-tempo grid. The Sync gate
    /// (<see cref="GridConfidenceCalculator"/>) reads it to decide beat/phase sync vs. tempo-only. Null on
    /// pre-v9 catalogs (unknown ⇒ phase sync preserved until re-analyzed); analysis persists the raw value
    /// so the gate threshold can be retuned without re-scanning.
    /// </summary>
    public double? GridCoherence { get; init; }

    /// <summary>
    /// Constant-tempo proof: the absolute BPM difference between the track's first and second halves
    /// (octave-folded to the same metrical level). Small ⇒ constant tempo (safe to phase-lock); large ⇒
    /// variable/live tempo (the grid drifts, so Sync downgrades to tempo-only). Null on pre-v9 catalogs.
    /// </summary>
    public double? TempoStabilityBpmDelta { get; init; }

    /// <summary>
    /// Kick-identity evidence for <see cref="FirstBeatSeconds"/>: how far the beat-synchronous low-band hump
    /// at the anchor stands above the hump half a beat away (<see cref="KickPhaseGate.MarginRatio"/>). Above
    /// 1 ⇒ the anchor is on the loud low-band event (the kick); below ⇒ it may be the off-beat, which is the
    /// error that flams a crossfade. Null on pre-v12 catalogs (unknown ⇒ prior behaviour preserved); the raw
    /// ratio is persisted so the gate threshold can be retuned without re-analyzing.
    /// </summary>
    public double? KickPhaseMarginRatio { get; init; }

    /// <summary>
    /// Stability evidence for <see cref="FirstBeatSeconds"/>: how far the phase fitted over one mid-file
    /// window sits from the whole-file phase, in seconds. Large ⇒ no single global phase exists (usually a
    /// declared tempo that is itself wrong), so no anchor can be right for the whole track. Null on pre-v12
    /// catalogs, or when the track was too short to fit a second window.
    /// </summary>
    public double? PhaseWindowDisagreementSeconds { get; init; }
}

/// <summary>
/// Orchestrates the BPM pipeline: mono PCM → onset envelope → tempo estimate → first-beat anchor.
/// Pure and hardware-free; the decode that produces the PCM lives behind <c>IAudioDecoder</c>.
/// </summary>
public sealed class BpmDetector
{
    private readonly OnsetEnvelope _onset;
    private readonly TempoEstimator _tempo;
    private readonly FirstBeatEstimator _firstBeat;
    private readonly IKickOnsetEnvelope _kickOnset;
    private readonly DownbeatEstimator _downbeat;
    private readonly GridRefiner _gridRefiner;
    private readonly IKickOnsetEnvelope _phaseOnset;
    private readonly BandEnergyEnvelope _bandEnergy = new();

    public BpmDetector(
        OnsetEnvelope? onset = null,
        TempoEstimator? tempo = null,
        FirstBeatEstimator? firstBeat = null,
        IKickOnsetEnvelope? kickOnset = null,
        DownbeatEstimator? downbeat = null,
        GridRefiner? gridRefiner = null,
        IKickOnsetEnvelope? phaseOnset = null)
    {
        _onset = onset ?? new OnsetEnvelope();
        _tempo = tempo ?? new TempoEstimator();
        _firstBeat = firstBeat ?? new FirstBeatEstimator();
        // Default to percussive (HPSS) separation so a sustained in-band bass / sub / 808 on the off-beat
        // cannot pollute the kick envelope (the band-only LowBandOnsetEnvelope remains as the simpler seam
        // impl / fallback). The kick anchor drives tempo refinement, beat phase, and the downbeat.
        _kickOnset = kickOnset ?? new PercussiveOnsetEnvelope();
        _downbeat = downbeat ?? new DownbeatEstimator();
        _gridRefiner = gridRefiner ?? new GridRefiner();
        // PHASE gets its OWN envelope, deliberately not the one tempo refinement runs on. On psytrance the
        // percussive (HPSS) envelope's beat-synchronous average peaks on the OFF-BEAT — its phase landed
        // within 5.8-20.7 ms of the >6 kHz hat peak on 4 of 11 measured tracks — because the off-beat layer
        // is every bit as percussive as the kick and louder. Re-picking the same estimator's onsets from the
        // low band agreed with an audio-derived reference within 8.1 ms on 9 of 11 tracks, against 2 of 11
        // for the shipped HPSS onsets. The band split cannot be used for TEMPO in its place: the refined BPM
        // (verified against Beatport on 12 tracks) comes from the HPSS fit, so the two stages read different
        // envelopes on purpose.
        _phaseOnset = phaseOnset ?? new LowBandOnsetEnvelope();
    }

    public BpmResult Detect(ReadOnlySpan<float> mono, int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        double[] envelope = _onset.Compute(mono);
        if (envelope.Length == 0)
            return new BpmResult(0, 0);

        double envelopeRateHz = _onset.EnvelopeRateHz(sampleRate);
        TempoEstimate estimate = _tempo.Estimate(envelope, envelopeRateHz);
        double bpm = estimate.Bpm;
        double confidence = estimate.Confidence;

        // The kick band drives the grid refinement, the beat-phase anchor, and the downbeat.
        double[] kickEnvelope = _kickOnset.Compute(mono, sampleRate);
        double kickRateHz = _kickOnset.EnvelopeRateHz(sampleRate);

        // Refine the TEMPO to the kicks: the autocorrelation tempo is quantized to integer envelope lags
        // (e.g. 139.67 for a true 140), which drifts a uniform grid off the kicks over a long track. The
        // refiner fits a sub-frame tempo + continuous phase by onset-phase coherence (and fixes octave/3:2
        // confusions); trust it only when the kick structure is strong (else the coarse tempo stands —
        // ambient/no-kick material).
        GridFit kickFit = bpm > 0 && kickEnvelope.Length > 0
            ? _gridRefiner.Refine(kickEnvelope, kickRateHz, bpm, 0.0)
            : new GridFit(bpm, 0.0, 0.0);
        bool kickTrusted = kickFit.Coherence >= GridRefiner.AcceptCoherence && kickFit.Bpm > 0;
        if (kickTrusted)
            bpm = kickFit.Bpm;

        // The tempo is final here, and everything after it (phase, downbeat, stability) is measured against
        // the tempo that actually gets published — a phase is an offset inside a beat, so it belongs to one
        // beat length only.
        bpm = Math.Round(bpm, 2);

        // Beat PHASE is measured on the LOW BAND and must EARN publication (v12). The tempo is final by now,
        // which is what lets phase use a different envelope: the anchor is re-derived from the kick band and
        // then gated, and a phase the gates cannot vouch for is REFUSED — analysis falls back to the shipped
        // anchor rather than emitting a confident wrong one. Neither the estimator's confidence nor the grid
        // coherence is used as that gate; see KickPhaseGate for why both are uninformative here.
        PhaseAnchor anchor = MeasurePhase(mono, sampleRate, bpm);

        // Fallbacks, in order: the kick fit's resultant phase (continuous/sub-frame) while the kick structure
        // is strong, else the broadband estimator, which is then the only phase signal there is.
        double firstBeatSeconds = anchor.Vouched
            ? WrapToBeat(anchor.PhaseSeconds, bpm)
            : kickTrusted
                ? WrapToBeat(kickFit.FirstBeatSeconds + _kickOnset.AnalysisLatencySeconds(sampleRate), bpm)
                : _firstBeat.Estimate(envelope, bpm, envelopeRateHz);
        firstBeatSeconds = Math.Round(firstBeatSeconds, 4);

        // Downbeat uses the (now-refined) tempo against the kick band; falls back to the beat anchor (no
        // bar phase) when the band can't form.
        DownbeatEstimate downbeat = kickEnvelope.Length > 0
            ? _downbeat.Estimate(kickEnvelope, bpm, kickRateHz, firstBeatSeconds)
            : new DownbeatEstimate(firstBeatSeconds, 4, 0.0);

        // The individual kick strike times, so a deck can later snap its grid onto the real kick nearest
        // the playhead (SET PHASE / one-shot SYNC), not just a global anchor. Rounded to the millisecond
        // to keep the catalog compact. They come from whichever envelope produced the published anchor:
        // a list half a beat off the grid it is snapped against is worse than no list.
        IReadOnlyList<double> kickOnsets = anchor.Vouched
            ? anchor.Onsets.Select(t => Math.Round(t, 3)).ToArray()
            : kickEnvelope.Length > 0
                ? KickOnsetPicker.Pick(kickEnvelope, kickRateHz, _kickOnset.AnalysisLatencySeconds(sampleRate))
                    .Select(t => Math.Round(t, 3)).ToArray()
                : Array.Empty<double>();

        return new BpmResult(bpm, Math.Round(confidence, 4), firstBeatSeconds)
        {
            DownbeatSeconds = Math.Round(downbeat.DownbeatSeconds, 4),
            BeatsPerBar = downbeat.BeatsPerBar,
            DownbeatConfidence = downbeat.Confidence,
            KickOnsetsSeconds = kickOnsets,
            // Grid-confidence signals for the Sync gate (SYNC-BEHAVIOR-SPEC §7). The kick-fit coherence
            // was previously discarded; carry it. Tempo stability is the one added signal — a constant-
            // tempo proof from the broadband envelope's two halves.
            GridCoherence = Math.Round(kickFit.Coherence, 4),
            TempoStabilityBpmDelta = TempoStabilityDelta(envelope, envelopeRateHz, bpm),
            // The phase evidence, raw: the Sync gate reads it to decide whether the anchor may be aligned on
            // (GridConfidenceCalculator), and persisting the raw numbers keeps the thresholds retunable.
            KickPhaseMarginRatio = anchor.MarginRatio is double margin ? Math.Round(margin, 3) : null,
            PhaseWindowDisagreementSeconds =
                anchor.WindowDisagreementSeconds is double drift ? Math.Round(drift, 4) : null,
        };
    }

    /// <summary>A measured beat phase plus the evidence for it and the onsets it came from.</summary>
    private readonly record struct PhaseAnchor(
        double PhaseSeconds,
        double? MarginRatio,
        double? WindowDisagreementSeconds,
        IReadOnlyList<double> Onsets)
    {
        public bool Vouched => KickPhaseGate.Passes(MarginRatio, WindowDisagreementSeconds);

        public static PhaseAnchor Unmeasurable { get; } = new(0.0, 0.0, null, Array.Empty<double>());
    }

    // Measure the beat phase on the kick band and gather the evidence the gates need. The margin is measured
    // on low-band AMPLITUDE (BandEnergyEnvelope.Low), not on the onset flux: the discriminator is which half
    // of the beat is LOUDER down there, and a flux envelope fires on the attack of a sustained off-beat bass
    // note just as it does on a kick.
    private PhaseAnchor MeasurePhase(ReadOnlySpan<float> mono, int sampleRate, double bpm)
    {
        if (bpm <= 0.0)
            return PhaseAnchor.Unmeasurable;

        double[] flux = _phaseOnset.Compute(mono, sampleRate);
        if (flux.Length == 0)
            return PhaseAnchor.Unmeasurable;

        IReadOnlyList<double> onsets = KickOnsetPicker.Pick(
            flux, _phaseOnset.EnvelopeRateHz(sampleRate), _phaseOnset.AnalysisLatencySeconds(sampleRate));
        KickPhase phase = KickPhaseEstimator.Estimate(onsets, bpm);
        if (phase.Total < KickPhaseEstimator.MinimumOnsets)
            return PhaseAnchor.Unmeasurable;

        BandEnergyFrames bands = _bandEnergy.Compute(mono, sampleRate);
        double margin = KickPhaseGate.MarginRatio(
            KickPhaseGate.BeatProfile(bands.Low, bands.FrameRateHz, bpm), bpm, phase.PhaseSeconds);
        double? disagreement = KickPhaseGate.WindowDisagreementSeconds(onsets, bpm, phase.PhaseSeconds);

        // Publish only the strikes that sit ON the anchor's phase. The band split cannot tell a kick from
        // another low hit, so the raw picks include off-grid material — and this list exists for SET PHASE to
        // snap a grid onto, where an off-grid entry is worse than a missing one.
        double[] onGrid = onsets
            .Where(t => Math.Abs(t - KickPhaseEstimator.SnapToPhase(t, phase.PhaseSeconds, bpm))
                        <= KickPhaseEstimator.DefaultToleranceSeconds)
            .ToArray();

        return new PhaseAnchor(phase.PhaseSeconds, margin, disagreement, onGrid);
    }

    // Constant-tempo proof: estimate the tempo over the first and second halves of the onset envelope and
    // return their octave-folded BPM difference. Small on a constant-tempo track; large when the tempo
    // drifts (live/acoustic) — the exact axis that separates "grids locally" from "stays constant" and the
    // reason vendors split static vs. dynamic beatgrids. Cheap (two autocorrelations on half the envelope).
    // 0 (assume stable) when too short to split or a half is undetectable — coherence still catches bad grids.
    private double TempoStabilityDelta(double[] envelope, double envelopeRateHz, double referenceBpm)
    {
        if (envelope.Length < 8 || referenceBpm <= 0.0)
            return 0.0;

        int half = envelope.Length / 2;
        double firstBpm = _tempo.Estimate(envelope[..half], envelopeRateHz).Bpm;
        double secondBpm = _tempo.Estimate(envelope[half..], envelopeRateHz).Bpm;
        if (firstBpm <= 0.0 || secondBpm <= 0.0)
            return 0.0;

        return Math.Round(Math.Abs(FoldToOctaveOf(firstBpm, referenceBpm) - FoldToOctaveOf(secondBpm, referenceBpm)), 3);
    }

    // Fold a BPM to the octave nearest the reference (√2 geometric midpoint), so a half's estimate landing
    // on a half/double octave is compared on the reference's metrical level rather than reading as drift.
    private static double FoldToOctaveOf(double bpm, double reference)
    {
        if (bpm <= 0.0 || reference <= 0.0)
            return bpm;
        while (bpm / reference >= 1.4142135623730951) bpm /= 2.0;
        while (bpm / reference < 0.7071067811865476) bpm *= 2.0;
        return bpm;
    }

    // Keep the first-beat anchor inside [0, 60/bpm): the kick fit's phase is taken modulo its own period,
    // but rounding the tempo afterwards shifts the beat length a hair, so re-wrap against the final tempo.
    private static double WrapToBeat(double offsetSeconds, double bpm)
    {
        if (bpm <= 0.0)
            return 0.0;
        double beatSeconds = 60.0 / bpm;
        double wrapped = offsetSeconds % beatSeconds;
        return wrapped < 0.0 ? wrapped + beatSeconds : wrapped;
    }
}
