using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Analysis.Structure;

namespace Liveolator.Core.Analysis;

/// <summary>Combined offline analysis of one track: tempo, musical key/scale, duration, cues, and the
/// song structure (doc 32).</summary>
/// <param name="Structure">Detected musical structure — from the built-in
/// <see cref="NoveltyStructureDetector"/>, or from the optional Python/librosa analyzer when it is
/// installed. <c>null</c> when the material is too flat or too short to read structure from.</param>
public sealed record TrackAnalysisResult(
    BpmResult Bpm, MusicalKey Key, TimeSpan Duration, TrackCues Cues, SongStructure? Structure = null);

/// <summary>
/// Measures BPM and musical key/scale from mono PCM, and (via <see cref="IAudioDecoder"/>)
/// from a file. Pure orchestration over <see cref="BpmDetector"/>, <see cref="ChromaExtractor"/>,
/// and <see cref="KeyClassifier"/> — the heart of the Track-Analysis module (doc 16).
/// </summary>
public sealed class TrackAnalyzer
{
    /// <summary>Increment when analyzer output semantics change and cached tracks must be refreshed.</summary>
    /// <remarks>v2: added the kick-band downbeat anchor (<see cref="Bpm.BpmResult.DownbeatSeconds"/>).
    /// v3: sub-frame tempo/phase refinement so the grid lands on the kicks (<see cref="Bpm.GridRefiner"/>) —
    /// fixes integer-lag drift (139.67→140) and octave/3:2 confusions.
    /// v4: beat PHASE now anchors on the kick (not the broadband envelope) with frame-centre latency
    /// compensation, and the kick onset uses percussive/HPSS separation (<see cref="Bpm.PercussiveOnsetEnvelope"/>)
    /// so an in-band bassline no longer pulls the grid off the down-beat (system review 2026-06-27). Existing
    /// tracks re-grid in the background on next scan.
    /// v5: optional offline song-structure segmentation (intro/buildup/drop/breakdown/outro) via the
    /// Python+librosa seam (<see cref="Structure.ISongStructureAnalyzer"/>, doc 32). Additive — when the
    /// runtime is absent, <see cref="TrackAnalysisResult.Structure"/> stays null and analysis is unchanged.
    /// v6: fast-tempo (&gt;~160 BPM) fix — the grid-refiner band reaches 180 and the tempo estimator promotes
    /// supported 2x/2.5x harmonics, so DnB-range tracks (168–180) no longer fold to ~70/87; previously
    /// mis-gridded fast tracks re-analyze.
    /// v7: persist the individual kick strike times (<see cref="Bpm.BpmResult.KickOnsetsSeconds"/>) so a
    /// deck can snap its grid onto the real kick nearest the playhead (SET PHASE / one-shot SYNC auto-align).
    /// Additive; existing tracks re-analyze on next scan to populate the kick list.
    /// v8: windowed + half-beat-harmonic grid refinement (<see cref="Bpm.GridRefiner"/>) — an offbeat
    /// bass or a mid-track arrangement edit no longer collapses the kick fit back to the quantized coarse
    /// bin (a true 145.0 read as 143.55, wrecking two-deck sync); mis-gridded tracks re-analyze.
    /// v9: persist the grid-confidence signals (<see cref="Bpm.BpmResult.GridCoherence"/> — previously
    /// discarded — and the new constant-tempo <see cref="Bpm.BpmResult.TempoStabilityBpmDelta"/>) so Sync
    /// can gate beat/phase sync on grid quality and downgrade an untrustworthy grid to tempo-only
    /// (SYNC-BEHAVIOR-SPEC §7). Additive; existing tracks re-analyze on next scan to populate the signals
    /// (until then the grid confidence reads Unknown and phase sync is preserved).
    /// v10: song structure is now detected in-process by <see cref="NoveltyStructureDetector"/>, so every
    /// track carries intro/build-up/drop/breakdown/outro sections without the optional Python runtime —
    /// which auto-cues, the STUDIO mix anchors and the library STRUCTURE badge all read. The Python
    /// analyzer still overrides it when installed. Existing tracks re-analyze on next scan.
    /// v11: key and tempo corrections (issues #4/#5). The chroma now ignores everything below the
    /// frequency its frame can actually resolve to a semitone (<see cref="Key.ChromaExtractor"/>) — the
    /// sub and kick used to dominate it with a fixed, music-independent pattern, so nearly every track
    /// classified as the same major key; the classifier moved to Temperley's usage-based profiles to
    /// match. <see cref="Bpm.TempoEstimator"/> also promotes the 1.5x (dotted) sub-harmonic, so a track
    /// whose accents fall every 1.5 beats no longer reads 4/3 fast. Existing tracks re-analyze on next
    /// scan; a track corrected by hand (<see cref="Library.Music.MusicTrack.AnalysisIsManual"/>) does not.</remarks>
    public const int CurrentVersion = 11;

    /// <summary>Sample rate the analysis pipeline runs at; decoders resample to this.</summary>
    public const int AnalysisSampleRate = 44100;

    private readonly BpmDetector _bpm;
    private readonly ChromaExtractor _chroma;
    private readonly KeyClassifier _key;
    private readonly SilenceCueDetector _cues;
    private readonly ISongStructureAnalyzer? _structure;
    private readonly BandEnergyEnvelope _bandEnergy = new();
    private readonly NoveltyStructureDetector _novelty = new();

    public TrackAnalyzer(
        BpmDetector? bpmDetector = null,
        ChromaExtractor? chromaExtractor = null,
        KeyClassifier? keyClassifier = null,
        SilenceCueDetector? cueDetector = null,
        ISongStructureAnalyzer? structureAnalyzer = null)
    {
        _bpm = bpmDetector ?? new BpmDetector();
        _chroma = chromaExtractor ?? new ChromaExtractor();
        _key = keyClassifier ?? new KeyClassifier();
        _cues = cueDetector ?? new SilenceCueDetector();
        _structure = structureAnalyzer;
    }

    /// <summary>Analyzes an in-memory mono PCM buffer.</summary>
    public TrackAnalysisResult AnalyzePcm(ReadOnlySpan<float> mono, int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        BpmResult bpm = _bpm.Detect(mono, sampleRate);
        double[] chroma = _chroma.Compute(mono, sampleRate);
        MusicalKey key = _key.Classify(chroma);
        TrackCues cues = _cues.Detect(mono, sampleRate);
        var duration = TimeSpan.FromSeconds((double)mono.Length / sampleRate);

        // Built-in structure pass: runs on the PCM already in hand, so it costs one band-energy STFT
        // and no second decode. Boundaries snap to the grid just measured. Null on material too
        // flat/short to read (callers keep their fallback).
        SongStructure? structure = _novelty.Detect(
            _bandEnergy.Compute(mono, sampleRate), BeatGrid.FromBpmResult(bpm));

        return new TrackAnalysisResult(bpm, key, duration, cues, structure);
    }

    /// <summary>
    /// Tempo-only analysis of a file: decode + <see cref="BpmDetector"/> and nothing else — no chroma
    /// FFT, key classification, cue scan, or structure pass. For latency-sensitive callers that only
    /// need the beat grid (a live deck load self-healing an unanalyzed track); the full
    /// <see cref="AnalyzeAsync"/> stays the catalog/scan path.
    /// </summary>
    public async Task<BpmResult> AnalyzeBpmAsync(
        IAudioDecoder decoder, string filePath, CancellationToken cancellationToken = default)
    {
        float[] buffer = await DecodeMonoAsync(decoder, filePath, cancellationToken).ConfigureAwait(false);
        return _bpm.Detect(buffer, AnalysisSampleRate);
    }

    /// <summary>
    /// Decodes a file to mono PCM through <paramref name="decoder"/> and analyzes it. Throws
    /// <see cref="NotSupportedException"/> if the decoder cannot handle the file.
    /// </summary>
    public async Task<TrackAnalysisResult> AnalyzeAsync(
        IAudioDecoder decoder, string filePath, CancellationToken cancellationToken = default)
    {
        float[] buffer = await DecodeMonoAsync(decoder, filePath, cancellationToken).ConfigureAwait(false);
        TrackAnalysisResult result = AnalyzePcm(buffer, AnalysisSampleRate);

        // Optional higher-quality structure pass (doc 32), which OVERRIDES the built-in novelty result
        // when it produces one. The analyzer is graceful by contract (null on missing runtime / failure),
        // so the built-in result stands unless librosa actually returned sections; the guard here only
        // keeps a buggy implementation from breaking core analysis.
        if (_structure is not null)
        {
            try
            {
                SongStructure? structure =
                    await _structure.AnalyzeAsync(decoder, filePath, cancellationToken).ConfigureAwait(false);
                if (structure is not null)
                    result = result with { Structure = structure };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Structure is a best-effort enrichment; never let it fail the core BPM/key/cue analysis.
            }
        }

        return result;
    }

    // One shared decode for both analysis entry points: whole file to mono PCM at the analysis rate.
    private static async Task<float[]> DecodeMonoAsync(
        IAudioDecoder decoder, string filePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        if (!decoder.CanDecode(filePath))
            throw new NotSupportedException($"Decoder cannot handle '{filePath}'.");

        var pcm = new List<float>();
        await foreach (ReadOnlyMemory<float> block in
            decoder.DecodeMonoAsync(filePath, AnalysisSampleRate, cancellationToken).ConfigureAwait(false))
        {
            pcm.AddRange(block.ToArray());
        }

        return pcm.ToArray();
    }
}
