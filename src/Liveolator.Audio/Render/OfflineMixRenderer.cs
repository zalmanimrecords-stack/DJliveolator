using Liveolator.Audio.Playback;
using Liveolator.Core.Analysis;
using Liveolator.Core.Mixer;
using Liveolator.Core.Studio;
using Liveolator.Core.Studio.Render;
using Microsoft.Extensions.Logging;

namespace Liveolator.Audio.Render;

/// <summary>
/// Renders a <see cref="StudioProject"/> arrangement to a mono WAV file offline: decodes each clip at
/// its warp factor (native rate via <see cref="IAudioDecoder"/> when unwarped; pitch-preserving
/// time-stretch via <see cref="BassFxRenderDecoder"/> when warped), then walks the output timeline
/// applying the pure <see cref="MixPlan"/> — per-deck gain, 3-band EQ, filter (the same
/// <see cref="MixerMath"/> coefficients the live mixer uses, through a stateful biquad cascade) — and
/// sums every deck into the master. Warp factor is constant per clip (sampled at its start). Mono MVP.
/// </summary>
public sealed class OfflineMixRenderer
{
    // Automation/coefficients are refreshed once per block (~6 ms at 44.1 kHz) — fine for envelopes.
    private const int BlockSize = 256;
    private const double UnwarpedEpsilon = 1e-4;

    private readonly IAudioDecoder _decoder;
    private readonly BassFxRenderDecoder _stretchDecoder;

    public OfflineMixRenderer(IAudioDecoder decoder, ILogger? logger = null)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _stretchDecoder = new BassFxRenderDecoder(logger);
    }

    // One decoded buffer per (clip path, warp factor): unwarped clips share the native-rate decode,
    // warped clips get a pitch-preserved, time-stretched buffer at the render rate.
    private static string SourceKey(string path, double factor) => $"{path}|{factor:F4}";

    /// <summary>
    /// Render <paramref name="project"/> to a 16-bit mono WAV at <paramref name="outputPath"/>.
    /// Reports 0..1 progress. An empty/zero-length project writes an empty WAV.
    /// </summary>
    public async Task RenderAsync(
        StudioProject project,
        string outputPath,
        int sampleRate = 44_100,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");

        var plan = new MixPlan(project);
        int totalSamples = plan.DurationSeconds > 0 ? (int)Math.Ceiling(plan.DurationSeconds * sampleRate) : 0;

        // Decode every distinct (clip, warp factor) once into a mono buffer at the render rate. Unwarped
        // clips use the managed decoder (identical to before); warped clips use BASS_FX (pitch preserved).
        var sources = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        foreach (StudioClip clip in project.Clips)
        {
            double factor = plan.WarpFactorFor(clip);
            string key = SourceKey(clip.TrackPath, factor);
            if (sources.ContainsKey(key))
                continue;

            sources[key] = Math.Abs(factor - 1.0) < UnwarpedEpsilon
                ? await DecodeAllAsync(clip.TrackPath, sampleRate, cancellationToken).ConfigureAwait(false)
                : _stretchDecoder.DecodeStretched(clip.TrackPath, sampleRate, (factor - 1.0) * 100.0);
        }

        var master = new float[totalSamples];
        int decks = plan.DeckCount;
        // Per-deck biquad cascade (low → mid → high → filter), mono (1 channel each).
        StatefulBiquad[] low = NewBiquads(decks), mid = NewBiquads(decks), high = NewBiquads(decks), filt = NewBiquads(decks);

        for (int blockStart = 0; blockStart < totalSamples; blockStart += BlockSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int blockLen = Math.Min(BlockSize, totalSamples - blockStart);
            double tBlock = blockStart / (double)sampleRate;

            for (int slot = 0; slot < decks; slot++)
            {
                DeckMixState state = plan.EvaluateDeck(slot, tBlock);
                if (!state.HasAudio || state.SourcePath is null ||
                    !sources.TryGetValue(SourceKey(state.SourcePath, state.WarpFactor), out float[]? src))
                    continue;

                low[slot].SetCoefficients(MixerMath.EqBandCoefficients(EqBand.Low, state.Eq, sampleRate));
                mid[slot].SetCoefficients(MixerMath.EqBandCoefficients(EqBand.Mid, state.Eq, sampleRate));
                high[slot].SetCoefficients(MixerMath.EqBandCoefficients(EqBand.High, state.Eq, sampleRate));
                filt[slot].SetCoefficients(MixerMath.FilterCoefficients(state.Filter, sampleRate));

                // The decoded buffer is already time-stretched to the project tempo, so it advances 1:1
                // with the timeline; the source-in trim maps into it scaled by the warp factor.
                double bufferSeconds = (state.SourceInSeconds / state.WarpFactor) + (tBlock - state.ClipStartSeconds);
                int srcStart = (int)Math.Round(bufferSeconds * sampleRate);
                for (int i = 0; i < blockLen; i++)
                {
                    int si = srcStart + i;
                    double x = (si >= 0 && si < src.Length ? src[si] : 0.0) * state.Gain;
                    x = filt[slot].Process(0, high[slot].Process(0, mid[slot].Process(0, low[slot].Process(0, x))));
                    master[blockStart + i] += (float)x;
                }
            }

            progress?.Report(totalSamples == 0 ? 1.0 : Math.Min(1.0, (blockStart + blockLen) / (double)totalSamples));
        }

        WavWriter.WriteMono(outputPath, master, sampleRate);
        progress?.Report(1.0);
    }

    private static StatefulBiquad[] NewBiquads(int count)
    {
        var biquads = new StatefulBiquad[count];
        for (int i = 0; i < count; i++)
            biquads[i] = new StatefulBiquad(channels: 1);
        return biquads;
    }

    private async Task<float[]> DecodeAllAsync(string path, int sampleRate, CancellationToken cancellationToken)
    {
        var samples = new List<float>();
        await foreach (ReadOnlyMemory<float> block in _decoder.DecodeMonoAsync(path, sampleRate, cancellationToken)
            .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int i = 0; i < block.Length; i++)
                samples.Add(block.Span[i]);
        }

        return samples.ToArray();
    }
}
