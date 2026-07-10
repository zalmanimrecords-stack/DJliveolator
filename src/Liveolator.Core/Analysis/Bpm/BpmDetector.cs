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

    public BpmDetector(
        OnsetEnvelope? onset = null,
        TempoEstimator? tempo = null,
        FirstBeatEstimator? firstBeat = null,
        IKickOnsetEnvelope? kickOnset = null,
        DownbeatEstimator? downbeat = null,
        GridRefiner? gridRefiner = null)
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

        // Beat PHASE anchors on the KICK, not the broadband envelope: the kick is the beat anchor two-deck
        // sync aligns to, so taking phase from broadband onsets (hats/vocals/stabs) pulls the grid off the
        // down-beat on bass-heavy material — the dominant cause of unsatisfying sync (system review 2026-06-27).
        // The kick fit's resultant phase is continuous (sub-frame). Fall back to the broadband estimator only
        // when the kick band is too weak to fit, where the broadband onset is the only phase signal we have.
        double firstBeatSeconds = kickTrusted
            ? WrapToBeat(kickFit.FirstBeatSeconds + _kickOnset.AnalysisLatencySeconds(sampleRate), bpm)
            : _firstBeat.Estimate(envelope, bpm, envelopeRateHz);
        bpm = Math.Round(bpm, 2);
        firstBeatSeconds = Math.Round(firstBeatSeconds, 4);

        // Downbeat uses the (now-refined) tempo against the kick band; falls back to the beat anchor (no
        // bar phase) when the band can't form.
        DownbeatEstimate downbeat = kickEnvelope.Length > 0
            ? _downbeat.Estimate(kickEnvelope, bpm, kickRateHz, firstBeatSeconds)
            : new DownbeatEstimate(firstBeatSeconds, 4, 0.0);

        // The individual kick strike times, so a deck can later snap its grid onto the real kick nearest
        // the playhead (SET PHASE / one-shot SYNC), not just a global anchor. Rounded to the millisecond
        // to keep the catalog compact.
        IReadOnlyList<double> kickOnsets = kickEnvelope.Length > 0
            ? KickOnsetPicker.Pick(kickEnvelope, kickRateHz, _kickOnset.AnalysisLatencySeconds(sampleRate))
                .Select(t => Math.Round(t, 3)).ToArray()
            : Array.Empty<double>();

        return new BpmResult(bpm, Math.Round(confidence, 4), firstBeatSeconds)
        {
            DownbeatSeconds = Math.Round(downbeat.DownbeatSeconds, 4),
            BeatsPerBar = downbeat.BeatsPerBar,
            DownbeatConfidence = downbeat.Confidence,
            KickOnsetsSeconds = kickOnsets,
        };
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
