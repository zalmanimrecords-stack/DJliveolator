using Liveolator.Audio.Playback;
using Liveolator.Core.Analysis;
using Liveolator.Core.Dsp;
using Liveolator.Core.Mixer;
using Liveolator.Core.Studio;
using Liveolator.Core.Studio.Render;
using Microsoft.Extensions.Logging;

namespace Liveolator.Audio.Render;

/// <summary>
/// Renders a <see cref="StudioProject"/> arrangement to a stereo WAV file offline: decodes each clip at
/// its warp factor (native rate via <see cref="IAudioDecoder"/> when unwarped - a mono source duplicated
/// to both channels; pitch-preserving stereo time-stretch via <see cref="BassFxRenderDecoder"/> when
/// warped), then walks the output timeline applying the pure <see cref="MixPlan"/> - per-deck gain,
/// 3-band EQ, filter (the same <see cref="MixerMath"/> coefficients the live mixer uses, through a
/// stateful biquad cascade with independent per-channel delay state, mirroring the realtime
/// <c>BassMixerChannel</c>) - and sums every deck into a stereo master. Warp factor is constant per clip
/// (sampled at its start). The summed master is then brick-wall limited (stereo-linked) and written as a
/// 2-channel WAV.
/// </summary>
public sealed class OfflineMixRenderer
{
    private const int OutputChannels = 2;     // stereo render
    private const int Left = 0;
    private const int Right = 1;

    // Automation/coefficients are refreshed once per block (~6 ms at 44.1 kHz) - fine for envelopes.
    private const int BlockSize = 256;
    private const double UnwarpedEpsilon = 1e-4;

    private readonly IAudioDecoder _decoder;
    private readonly BassFxRenderDecoder _stretchDecoder;

    // Optional decode override (tests): supplies a StereoBuffer for a (path, warpFactor) so the renderer
    // can be exercised with distinct L/R content without real BASS. Null in production.
    private readonly Func<string, double, StereoBuffer>? _decodeOverride;

    public OfflineMixRenderer(IAudioDecoder decoder, ILogger? logger = null)
        : this(decoder, logger, decodeOverride: null)
    {
    }

    internal OfflineMixRenderer(IAudioDecoder decoder, ILogger? logger, Func<string, double, StereoBuffer>? decodeOverride)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _stretchDecoder = new BassFxRenderDecoder(logger);
        _decodeOverride = decodeOverride;
    }

    // One decoded buffer per (clip path, warp factor): unwarped clips share the native-rate decode,
    // warped clips get a pitch-preserved, time-stretched buffer at the render rate.
    private static string SourceKey(string path, double factor) => $"{path}|{factor:F4}";

    // Identity of the source currently sounding on a deck, used to decide when the persistent biquad
    // cascade must restart from zero history. Two clips that share a track + warp are still distinct
    // sources when their timeline anchor or source-in differs, so the timeline start and source-in are
    // part of the key - a new clip never inherits the previous clip's filter ring.
    private static string ActiveSourceKey(DeckMixState state)
        => $"{SourceKey(state.SourcePath!, state.WarpFactor)}|{state.ClipStartSeconds:F6}|{state.SourceInSeconds:F6}";

    /// <summary>
    /// Render <paramref name="project"/> to a 16-bit stereo WAV at <paramref name="outputPath"/>.
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

        // Decode every distinct (clip, warp factor) once into a stereo buffer at the render rate. Unwarped
        // clips use the managed mono decoder duplicated to both channels; warped clips use BASS_FX stereo.
        var sources = new Dictionary<string, StereoBuffer>(StringComparer.OrdinalIgnoreCase);
        foreach (StudioClip clip in project.Clips)
        {
            double factor = plan.WarpFactorFor(clip);
            string key = SourceKey(clip.TrackPath, factor);
            if (sources.ContainsKey(key))
                continue;

            sources[key] = await DecodeSourceAsync(clip.TrackPath, factor, sampleRate, cancellationToken)
                .ConfigureAwait(false);
        }

        // Interleaved stereo master (L0,R0,L1,R1,...) so the stereo-linked limiter sees both channels.
        var master = new float[totalSamples * OutputChannels];
        int decks = plan.DeckCount;
        // Per-deck biquad cascade (low -> mid -> high -> filter), each a single 2-channel StatefulBiquad
        // addressed by channel index so L and R carry independent delay state (mirrors BassMixerChannel).
        // The delay state PERSISTS across every block for the duration of a deck's continuous source, so
        // filtering across a block boundary is identical to one continuous pass (the live mixer never
        // resets state per block). Only the coefficients are refreshed per block (from automation).
        StatefulBiquad[] low = NewBiquads(decks), mid = NewBiquads(decks), high = NewBiquads(decks), filt = NewBiquads(decks);
        // The source currently feeding each deck's cascade. When a deck's active source changes (it went
        // silent, or a different clip's source took over), its biquads are recreated so the new source
        // starts from zero delay history - mirroring a freshly loaded live stream, never inheriting the
        // previous source's filter ring. StatefulBiquad is intentionally not reset in place (Core-owned).
        var deckSource = new string?[decks];

        for (int blockStart = 0; blockStart < totalSamples; blockStart += BlockSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int blockLen = Math.Min(BlockSize, totalSamples - blockStart);
            double tBlock = blockStart / (double)sampleRate;

            for (int slot = 0; slot < decks; slot++)
            {
                DeckMixState state = plan.EvaluateDeck(slot, tBlock);
                if (!state.HasAudio || state.SourcePath is null ||
                    !sources.TryGetValue(SourceKey(state.SourcePath, state.WarpFactor), out StereoBuffer? src))
                {
                    // Deck is silent this block: its next sounding clip is a genuine source discontinuity,
                    // so drop the persistent state (a fresh stream would start at zero).
                    deckSource[slot] = null;
                    continue;
                }

                // Identify the active source on this deck. A different clip (path, warp, or timeline anchor)
                // is a discontinuity even on the same deck slot, so recreate the cascade from zero history.
                string activeSource = ActiveSourceKey(state);
                if (deckSource[slot] != activeSource)
                {
                    low[slot] = new StatefulBiquad(OutputChannels);
                    mid[slot] = new StatefulBiquad(OutputChannels);
                    high[slot] = new StatefulBiquad(OutputChannels);
                    filt[slot] = new StatefulBiquad(OutputChannels);
                    deckSource[slot] = activeSource;
                }

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
                    bool inRange = si >= 0 && si < src.Length;

                    // Process each channel through its own delay line: filter(high(mid(low(x)))) per L/R.
                    double l = (inRange ? src.Left[si] : 0.0) * state.Gain;
                    l = filt[slot].Process(Left, high[slot].Process(Left, mid[slot].Process(Left, low[slot].Process(Left, l))));

                    double r = (inRange ? src.Right[si] : 0.0) * state.Gain;
                    r = filt[slot].Process(Right, high[slot].Process(Right, mid[slot].Process(Right, low[slot].Process(Right, r))));

                    int frame = (blockStart + i) * OutputChannels;
                    master[frame + Left] += (float)l;
                    master[frame + Right] += (float)r;
                }
            }

            progress?.Report(totalSamples == 0 ? 1.0 : Math.Min(1.0, (blockStart + blockLen) / (double)totalSamples));
        }

        ApplyMasterLimiter(master, sampleRate);

        WriteStereo(outputPath, master, totalSamples, sampleRate);
        progress?.Report(1.0);
    }

    // Decode one (path, warp factor) to a stereo buffer at the render rate. Unwarped: the managed mono
    // decoder duplicated to both channels (CI-safe, deterministic, no native). Warped: BASS_FX stereo.
    // A test decode override (when present) supplies the buffer directly so distinct L/R can be injected.
    private async Task<StereoBuffer> DecodeSourceAsync(
        string path, double factor, int sampleRate, CancellationToken cancellationToken)
    {
        if (_decodeOverride is not null)
            return _decodeOverride(path, factor);

        if (Math.Abs(factor - 1.0) < UnwarpedEpsilon)
            return StereoBuffer.FromMono(await DecodeAllAsync(path, sampleRate, cancellationToken).ConfigureAwait(false));

        return _stretchDecoder.DecodeStretchedStereo(path, sampleRate, (factor - 1.0) * 100.0);
    }

    // Run the summed interleaved-stereo master through the same brick-wall limiter the realtime master bus
    // uses (BassMixerBackend.OnMasterDsp), constructed for 2 channels so it is stereo-linked - a multi-deck
    // mix that sums past full scale never clips in the exported WAV. The limiter delays its output by
    // LatencySamples (its look-ahead), so we process the master plus one look-ahead window of trailing
    // silence to flush the delay line, then copy back the audio that occupies
    // [latencyFloats, latencyFloats + master.Length), keeping the rendered length identical to the input
    // and the tail un-truncated. Limits in place.
    private static void ApplyMasterLimiter(float[] master, int sampleRate)
    {
        if (master.Length == 0)
            return;

        var limiter = new MasterLimiter(sampleRate, channels: OutputChannels);
        int latencyFloats = limiter.LatencySamples * OutputChannels;

        var work = new float[master.Length + latencyFloats];
        Array.Copy(master, work, master.Length);
        limiter.Process(work);

        // output[i] == input[i - latency]; the real audio occupies [latencyFloats, latencyFloats + length).
        Array.Copy(work, latencyFloats, master, 0, master.Length);
    }

    private static void WriteStereo(string outputPath, float[] interleaved, int frames, int sampleRate)
    {
        var left = new float[frames];
        var right = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            left[i] = interleaved[(i * OutputChannels) + Left];
            right[i] = interleaved[(i * OutputChannels) + Right];
        }
        WavWriter.WriteStereo(outputPath, left, right, sampleRate);
    }

    private static StatefulBiquad[] NewBiquads(int count)
    {
        var biquads = new StatefulBiquad[count];
        for (int i = 0; i < count; i++)
            biquads[i] = new StatefulBiquad(channels: OutputChannels);
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
