using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Structure;

namespace Liveolator.Core.Analysis.Cues;

/// <summary>
/// Produces a track's auto hot-cue set from audio: decodes to mono PCM (via <see cref="IAudioDecoder"/>),
/// measures tempo and the audible region, reads the band-energy contour, detects the musical structure,
/// and places the cues into the 8-slot bank. Pure orchestration over the analysis primitives — no UI, no
/// native, no persistence — so it unit-tests with a fake decoder (doc 16, Core iron rule #1).
/// <para>
/// When a real <see cref="SongStructure"/> is supplied (offline Python/librosa segmentation, doc 32) the
/// cues anchor on its section boundaries instead of the heuristic <see cref="StructuralCueDetector"/>;
/// with no structure it falls back to the heuristic path unchanged.
/// </para>
/// </summary>
public sealed class AutoCueAnalyzer
{
    private readonly BpmDetector _bpm;
    private readonly SilenceCueDetector _silence;
    private readonly BandEnergyEnvelope _bandEnergy;
    private readonly StructuralCueDetector _structural;
    private readonly AutoCuePlacer _placer;

    public AutoCueAnalyzer(
        BpmDetector? bpmDetector = null,
        SilenceCueDetector? silenceDetector = null,
        BandEnergyEnvelope? bandEnergy = null,
        StructuralCueDetector? structuralDetector = null,
        AutoCuePlacer? placer = null)
    {
        _bpm = bpmDetector ?? new BpmDetector();
        _silence = silenceDetector ?? new SilenceCueDetector();
        _bandEnergy = bandEnergy ?? new BandEnergyEnvelope();
        _structural = structuralDetector ?? new StructuralCueDetector();
        _placer = placer ?? new AutoCuePlacer();
    }

    /// <summary>
    /// Computes auto cues from an in-memory mono PCM buffer. Returns null when the tempo is undetectable
    /// (no beat grid to anchor cues to) — the caller then leaves the track's cues untouched. When
    /// <paramref name="structure"/> is supplied its real section boundaries are used in place of the
    /// heuristic structural detector.
    /// </summary>
    public TrackCueSet? AnalyzePcm(ReadOnlySpan<float> mono, int sampleRate, SongStructure? structure = null)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        BpmResult bpm = _bpm.Detect(mono, sampleRate);
        TrackCues silence = _silence.Detect(mono, sampleRate);

        // Nothing to cue when there is no beat grid (undetectable tempo) or no audible region at all
        // (silence/near-silence) — leave the track's cues untouched rather than place cues at sample 0.
        if (bpm.Bpm <= 0.0 || silence.IntroStart is null)
            return null;

        // Prefer real ML section boundaries when available; otherwise read structure from the energy contour.
        StructuralCueResult cues = SongStructureCues.ToStructuralCues(structure)
            ?? _structural.Detect(_bandEnergy.Compute(mono, sampleRate), bpm, silence, (double)mono.Length / sampleRate);

        return _placer.Place(cues, bpm.Bpm, sampleRate);
    }

    /// <summary>
    /// Decodes a file through <paramref name="decoder"/> and computes its auto cues. Returns null when the
    /// tempo is undetectable. Throws <see cref="NotSupportedException"/> if the decoder cannot handle the
    /// file. Runs entirely off the audio thread (offline decode), so it is safe for a background pass.
    /// When <paramref name="structure"/> is supplied its real section boundaries anchor the cues.
    /// </summary>
    public async Task<TrackCueSet?> AnalyzeAsync(
        IAudioDecoder decoder, string filePath, SongStructure? structure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        if (!decoder.CanDecode(filePath))
            throw new NotSupportedException($"Decoder cannot handle '{filePath}'.");

        var pcm = new List<float>();
        await foreach (ReadOnlyMemory<float> block in
            decoder.DecodeMonoAsync(filePath, TrackAnalyzer.AnalysisSampleRate, cancellationToken).ConfigureAwait(false))
        {
            pcm.AddRange(block.ToArray());
        }

        return AnalyzePcm(pcm.ToArray(), TrackAnalyzer.AnalysisSampleRate, structure);
    }
}
