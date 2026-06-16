using ManagedBass;
using ManagedBass.Fx;
using ManagedBass.Mix;
using Microsoft.Extensions.Logging;

namespace Liveolator.Audio.Render;

/// <summary>
/// Decodes a track and time-stretches it with BASS_FX (SoundTouch) — tempo changed, pitch preserved
/// (keylock) — for the STUDIO offline render's warp. The decode stream → a BASS_FX tempo stream → a
/// BASSmix mixer that resamples to the render rate and downmixes to mono (matching the rest of the
/// renderer). Native; BASS must already be initialised (it is, inside the running app). Failures
/// degrade to an empty buffer with a warning, never a throw (global #16/#26).
/// </summary>
public sealed class BassFxRenderDecoder
{
    private const int PullFloats = 8192;

    private readonly ILogger? _log;

    public BassFxRenderDecoder(ILogger? logger = null) => _log = logger;

    /// <summary>
    /// Decode <paramref name="path"/> at <paramref name="sampleRate"/> (mono), time-stretched by
    /// <paramref name="tempoPercent"/> (e.g. +16.7 for 120→140 BPM) with pitch preserved.
    /// </summary>
    public float[] DecodeStretched(string path, int sampleRate, double tempoPercent)
    {
        int decode = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float);
        if (decode == 0)
        {
            _log?.LogWarning("STUDIO warp: BASS CreateStream('{Path}') failed: {Error}.", path, Bass.LastError);
            return Array.Empty<float>();
        }

        int tempo = BassFx.TempoCreate(decode, BassFlags.Decode | BassFlags.FxFreeSource);
        if (tempo == 0)
        {
            _log?.LogWarning("STUDIO warp: BASS_FX TempoCreate failed: {Error}.", Bass.LastError);
            Bass.StreamFree(decode);
            return Array.Empty<float>();
        }

        Bass.ChannelSetAttribute(tempo, ChannelAttribute.Tempo, (float)tempoPercent);

        // Mono mixer at the render rate resamples the (file-rate, possibly-stereo) tempo stream to match.
        int mixer = BassMix.CreateMixerStream(sampleRate, 1, BassFlags.Decode | BassFlags.Float);
        if (mixer == 0)
        {
            _log?.LogWarning("STUDIO warp: CreateMixerStream failed: {Error}.", Bass.LastError);
            Bass.StreamFree(tempo);
            return Array.Empty<float>();
        }

        if (!BassMix.MixerAddChannel(mixer, tempo, BassFlags.Default))
        {
            _log?.LogWarning("STUDIO warp: MixerAddChannel failed: {Error}.", Bass.LastError);
            Bass.StreamFree(mixer);
            Bass.StreamFree(tempo);
            return Array.Empty<float>();
        }

        var samples = new List<float>();
        var buffer = new float[PullFloats];
        while (true)
        {
            int bytes = Bass.ChannelGetData(mixer, buffer, PullFloats * sizeof(float));
            if (bytes <= 0)
                break; // -1 = error/end, 0 = no data
            int got = bytes / sizeof(float);
            for (int i = 0; i < got; i++)
                samples.Add(buffer[i]);
        }

        Bass.StreamFree(mixer);
        Bass.StreamFree(tempo); // FxFreeSource frees the underlying decode stream too
        return samples.ToArray();
    }
}
