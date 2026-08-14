using ManagedBass;
using ManagedBass.Fx;
using ManagedBass.Mix;
using Microsoft.Extensions.Logging;

namespace Liveolator.Audio.Render;

/// <summary>
/// Decodes a track and time-stretches it with BASS_FX (SoundTouch) - tempo changed, pitch preserved
/// (keylock) - for the STUDIO offline render's warp. The decode stream -> a BASS_FX tempo stream -> a
/// BASSmix mixer that resamples to the render rate and produces interleaved stereo (matching the rest of
/// the stereo renderer; a mono source is upmixed to both channels by the mixer). Native; BASS is brought
/// up on demand through <see cref="BassAudioDecoder.EnsureUsable"/> (the same tolerant no-sound init the
/// offline decoder uses), so a host that renders without a playback device - the MCP server - gets audio
/// rather than a silent mix. Failures degrade to an empty buffer with a warning, never a throw
/// (global #16/#26); the empty buffer is what <see cref="OfflineMixRenderer"/> counts and reports, so a
/// failed decode can no longer pass for a rendered mix.
/// </summary>
public sealed class BassFxRenderDecoder
{
    private const int RenderChannels = 2;     // stereo render output (interleaved L/R)
    private const int PullFloats = 8192;

    private readonly ILogger? _log;

    public BassFxRenderDecoder(ILogger? logger = null) => _log = logger;

    /// <summary>
    /// Decode <paramref name="path"/> at <paramref name="sampleRate"/> (stereo), time-stretched by
    /// <paramref name="tempoPercent"/> (e.g. +16.7 for 120-&gt;140 BPM) with pitch preserved. The result is
    /// split into equal-length left/right channel buffers; an empty result means the decode failed.
    /// Decoding stops once <paramref name="maxFrames"/> stereo frames have been produced, so a clip that
    /// only uses the head of a long track does not pull (and hold) the whole file in memory.
    /// </summary>
    internal StereoBuffer DecodeStretchedStereo(string path, int sampleRate, double tempoPercent, int maxFrames = int.MaxValue)
    {
        // The render host need not have a playback device up: a no-sound init is enough for decode streams,
        // and skipping it is exactly how an offline render silently produced 69 minutes of nothing.
        if (!BassAudioDecoder.EnsureUsable())
        {
            _log?.LogWarning(
                "STUDIO warp: BASS is unavailable (init failed or the native library is missing), so '{Path}' " +
                "cannot be time-stretched and would render as silence.", path);
            return Empty();
        }

        int decode = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float);
        if (decode == 0)
        {
            _log?.LogWarning("STUDIO warp: BASS CreateStream('{Path}') failed: {Error}.", path, Bass.LastError);
            return Empty();
        }

        int tempo = BassFx.TempoCreate(decode, BassFlags.Decode | BassFlags.FxFreeSource);
        if (tempo == 0)
        {
            _log?.LogWarning("STUDIO warp: BASS_FX TempoCreate failed: {Error}.", Bass.LastError);
            Bass.StreamFree(decode);
            return Empty();
        }

        Bass.ChannelSetAttribute(tempo, ChannelAttribute.Tempo, (float)tempoPercent);

        // Stereo mixer at the render rate resamples the (file-rate, mono-or-stereo) tempo stream to match;
        // BASSmix upmixes a mono source to both channels, so the output is always interleaved L/R.
        int mixer = BassMix.CreateMixerStream(sampleRate, RenderChannels, BassFlags.Decode | BassFlags.Float);
        if (mixer == 0)
        {
            _log?.LogWarning("STUDIO warp: CreateMixerStream failed: {Error}.", Bass.LastError);
            Bass.StreamFree(tempo);
            return Empty();
        }

        if (!BassMix.MixerAddChannel(mixer, tempo, BassFlags.Default))
        {
            _log?.LogWarning("STUDIO warp: MixerAddChannel failed: {Error}.", Bass.LastError);
            Bass.StreamFree(mixer);
            Bass.StreamFree(tempo);
            return Empty();
        }

        // Cap the pull at the frames the caller needs (+ a small margin for block-boundary rounding), so a
        // trimmed clip on a long track holds only what it plays. int.MaxValue ⇒ decode the whole stream.
        long maxFloats = maxFrames >= int.MaxValue / RenderChannels
            ? long.MaxValue
            : (long)(maxFrames + PullFloats) * RenderChannels;
        var interleaved = new List<float>();
        var buffer = new float[PullFloats];
        while (interleaved.Count < maxFloats)
        {
            int bytes = Bass.ChannelGetData(mixer, buffer, PullFloats * sizeof(float));
            if (bytes <= 0)
                break; // -1 = error/end, 0 = no data
            int got = bytes / sizeof(float);
            for (int i = 0; i < got; i++)
                interleaved.Add(buffer[i]);
        }

        Bass.StreamFree(mixer);
        Bass.StreamFree(tempo); // FxFreeSource frees the underlying decode stream too
        return Deinterleave(interleaved);
    }

    // Split interleaved L/R into two equal-length channel buffers; a complete stereo stream yields an
    // even count, so integer division drops at most one dangling sample defensively.
    private static StereoBuffer Deinterleave(List<float> interleaved)
    {
        int frames = interleaved.Count / RenderChannels;
        var left = new float[frames];
        var right = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            left[i] = interleaved[(i * RenderChannels) + 0];
            right[i] = interleaved[(i * RenderChannels) + 1];
        }
        return new StereoBuffer(left, right);
    }

    private static StereoBuffer Empty() => new(Array.Empty<float>(), Array.Empty<float>());
}
