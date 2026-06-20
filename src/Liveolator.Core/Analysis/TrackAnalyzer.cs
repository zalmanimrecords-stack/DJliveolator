using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;

namespace Liveolator.Core.Analysis;

/// <summary>Combined offline analysis of one track: tempo, musical key/scale, duration, cues.</summary>
public sealed record TrackAnalysisResult(BpmResult Bpm, MusicalKey Key, TimeSpan Duration, TrackCues Cues);

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
    /// fixes integer-lag drift (139.67→140) and octave/3:2 confusions; existing tracks re-grid in background.</remarks>
    public const int CurrentVersion = 3;

    /// <summary>Sample rate the analysis pipeline runs at; decoders resample to this.</summary>
    public const int AnalysisSampleRate = 44100;

    private readonly BpmDetector _bpm;
    private readonly ChromaExtractor _chroma;
    private readonly KeyClassifier _key;
    private readonly SilenceCueDetector _cues;

    public TrackAnalyzer(
        BpmDetector? bpmDetector = null,
        ChromaExtractor? chromaExtractor = null,
        KeyClassifier? keyClassifier = null,
        SilenceCueDetector? cueDetector = null)
    {
        _bpm = bpmDetector ?? new BpmDetector();
        _chroma = chromaExtractor ?? new ChromaExtractor();
        _key = keyClassifier ?? new KeyClassifier();
        _cues = cueDetector ?? new SilenceCueDetector();
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
    /// Decodes a file to mono PCM through <paramref name="decoder"/> and analyzes it. Throws
    /// <see cref="NotSupportedException"/> if the decoder cannot handle the file.
    /// </summary>
    public async Task<TrackAnalysisResult> AnalyzeAsync(
        IAudioDecoder decoder, string filePath, CancellationToken cancellationToken = default)
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

        var buffer = pcm.ToArray();
        return AnalyzePcm(buffer, AnalysisSampleRate);
    }
}
