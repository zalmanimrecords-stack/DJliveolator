using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Analysis.Structure;

namespace Liveolator.Core.Analysis;

/// <summary>Combined offline analysis of one track: tempo, musical key/scale, duration, cues, and
/// (when the optional Python/librosa analyzer is available) the song structure (doc 32).</summary>
/// <param name="Structure">Detected musical structure, or <c>null</c> when structure analysis was not
/// run or was unavailable — existing analysis without Python keeps working unchanged.</param>
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
    /// mis-gridded fast tracks re-analyze.</remarks>
    public const int CurrentVersion = 6;

    /// <summary>Sample rate the analysis pipeline runs at; decoders resample to this.</summary>
    public const int AnalysisSampleRate = 44100;

    private readonly BpmDetector _bpm;
    private readonly ChromaExtractor _chroma;
    private readonly KeyClassifier _key;
    private readonly SilenceCueDetector _cues;
    private readonly ISongStructureAnalyzer? _structure;

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
        return new TrackAnalysisResult(bpm, key, duration, cues);
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

        // Optional song-structure pass (doc 32). The analyzer is graceful by contract (null on missing
        // runtime / failure); the guard here only keeps a buggy implementation from breaking core analysis.
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
