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

    /// <summary>The latency probe: a click this far into a track this long, searched this far either side.</summary>
    private const double ProbeLengthSeconds = 4.0;
    private const double ProbeClickSeconds = 2.0;
    private const double ProbeSearchSeconds = 0.5;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(long Tempo, int Rate), double>
        LatencyCache = new();

    /// <summary>Below this the clip is at the project tempo and no time-stretch pass is built.</summary>
    private const double TempoEpsilon = 1e-4;

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
        => Realign(
            DecodeStretchedRaw(path, sampleRate, tempoPercent, maxFrames),
            LatencySeconds(tempoPercent, sampleRate),
            sampleRate);

    private StereoBuffer DecodeStretchedRaw(string path, int sampleRate, double tempoPercent, int maxFrames)
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

        // A clip already at the project tempo needs no time-stretching: feed the decode stream straight to
        // the mixer rather than through a WSOLA pass whose ratio happens to be 1:1.
        int source = decode;
        if (Math.Abs(tempoPercent) > TempoEpsilon)
        {
            int tempo = BassFx.TempoCreate(decode, BassFlags.Decode | BassFlags.FxFreeSource);
            if (tempo == 0)
            {
                _log?.LogWarning("STUDIO warp: BASS_FX TempoCreate failed: {Error}.", Bass.LastError);
                Bass.StreamFree(decode);
                return Empty();
            }

            Bass.ChannelSetAttribute(tempo, ChannelAttribute.Tempo, (float)tempoPercent);
            source = tempo;
        }

        // Stereo mixer at the render rate resamples the (file-rate, mono-or-stereo) tempo stream to match;
        // BASSmix upmixes a mono source to both channels, so the output is always interleaved L/R.
        int mixer = BassMix.CreateMixerStream(sampleRate, RenderChannels, BassFlags.Decode | BassFlags.Float);
        if (mixer == 0)
        {
            _log?.LogWarning("STUDIO warp: CreateMixerStream failed: {Error}.", Bass.LastError);
            Bass.StreamFree(source);
            return Empty();
        }

        if (!BassMix.MixerAddChannel(mixer, source, BassFlags.Default))
        {
            _log?.LogWarning("STUDIO warp: MixerAddChannel failed: {Error}.", Bass.LastError);
            Bass.StreamFree(mixer);
            Bass.StreamFree(source);
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
        Bass.StreamFree(source); // a tempo stream (FxFreeSource) frees the underlying decode stream too
        return Deinterleave(interleaved);
    }

    /// <summary>
    /// How far the stretcher's output runs AHEAD of where the render geometry places it (source second s at
    /// output second s/factor). MEASURED, not modelled: a click at a known position is pushed through the
    /// same pass and its arrival compared. Measured on BASS_FX at 44.1 kHz it is 7-10 ms and it MOVES with
    /// the tempo, which is what matters — a constant offset would shift a whole mix and nobody would hear
    /// it, but two decks stretched by different amounts land milliseconds apart, and that is a flam on the
    /// kick. Cached per (tempo, rate): the probe costs one two-second decode.
    /// </summary>
    private double LatencySeconds(double tempoPercent, int sampleRate)
    {
        // The unstretched path bypasses the tempo stream entirely, so it has nothing to correct — and
        // measured exactly 0.0 ms, which is what makes the numbers below trustworthy.
        if (Math.Abs(tempoPercent) <= TempoEpsilon)
            return 0.0;

        (long, int) key = ((long)Math.Round(tempoPercent * 1000.0), sampleRate);
        return LatencyCache.GetOrAdd(key, _ => MeasureLatencySeconds(tempoPercent, sampleRate));
    }

    private double MeasureLatencySeconds(double tempoPercent, int sampleRate)
    {
        // A fresh unique name, never a fixed one: the temp directory is shared between users on Linux and
        // macOS, so a predictable path lets another local account pre-place the file — the measurement would
        // then be taken from THEIR audio, or FileMode.Create would write through their symlink. Writing it
        // per measurement costs nothing: LatencyCache means this runs once per (tempo, rate).
        string probe = Path.Combine(Path.GetTempPath(), $"liveolator-stretch-probe-{Guid.NewGuid():N}.wav");
        try
        {
            WriteClickProbe(probe, sampleRate);

            StereoBuffer stretched = DecodeStretchedRaw(probe, sampleRate, tempoPercent, int.MaxValue);
            if (stretched.Length == 0)
                return 0.0;

            double expected = ProbeClickSeconds / (1.0 + (tempoPercent / 100.0));
            int from = Math.Max(0, (int)((expected - ProbeSearchSeconds) * sampleRate));
            int to = Math.Min(stretched.Length, (int)((expected + ProbeSearchSeconds) * sampleRate));
            int peak = -1;
            float loudest = 0f;
            for (int i = from; i < to; i++)
            {
                float value = Math.Abs(stretched.Left[i]);
                if (value > loudest)
                {
                    loudest = value;
                    peak = i;
                }
            }

            // Nothing found means the probe itself failed; correcting by a guess would be worse than not
            // correcting at all.
            return peak < 0 || loudest <= 0f ? 0.0 : (peak / (double)sampleRate) - expected;
        }
        catch (IOException ex)
        {
            _log?.LogWarning(ex, "STUDIO warp: could not measure the stretcher's latency; leaving it uncorrected.");
            return 0.0;
        }
        catch (UnauthorizedAccessException ex)
        {
            _log?.LogWarning(ex, "STUDIO warp: could not measure the stretcher's latency; leaving it uncorrected.");
            return 0.0;
        }
        finally
        {
            try
            {
                File.Delete(probe);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A probe left behind costs a few hundred KB of temp space and nothing else, so this must
                // not fail a render — but it is logged rather than swallowed.
                _log?.LogDebug(ex, "STUDIO warp: could not delete the latency probe '{Path}'.", probe);
            }
        }
    }

    // Push the buffer later by the amount the stretcher ran early (latency is negative when it is early).
    private static StereoBuffer Realign(StereoBuffer buffer, double latencySeconds, int sampleRate)
    {
        int shift = (int)Math.Round(-latencySeconds * sampleRate);
        if (buffer.Length == 0 || shift == 0)
            return buffer;

        var left = new float[buffer.Length];
        var right = new float[buffer.Length];
        for (int i = 0; i < buffer.Length; i++)
        {
            int source = i - shift;
            if (source < 0 || source >= buffer.Length)
                continue;
            left[i] = buffer.Left[source];
            right[i] = buffer.Right[source];
        }

        return new StereoBuffer(left, right);
    }

    // Mono 16-bit silence with one short full-scale click, the smallest signal whose arrival can be timed.
    private static void WriteClickProbe(string path, int sampleRate)
    {
        int frames = (int)(ProbeLengthSeconds * sampleRate);
        var samples = new short[frames];
        int at = (int)(ProbeClickSeconds * sampleRate);
        for (int i = 0; i < 8 && at + i < frames; i++)
            samples[at + i] = short.MaxValue;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);
        int dataBytes = samples.Length * sizeof(short);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataBytes);
        writer.Write("WAVEfmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataBytes);
        foreach (short sample in samples)
            writer.Write(sample);
    }

    /// <summary>
    /// Decode <paramref name="path"/> from <paramref name="sourceInSeconds"/> onward at a tempo that MOVES:
    /// <paramref name="tempoPercentAt"/> is asked, per pulled block, for the stretch that block should
    /// carry, given how many seconds of OUTPUT have been produced so far. Because the output plays 1:1 with
    /// the project timeline, that argument IS the elapsed timeline second of the clip — so the returned
    /// buffer starts exactly at the clip's timeline start and is read with no offset arithmetic at all.
    /// <para>Seeking BEFORE the stretch rather than indexing into a stretched whole file also removes the
    /// <c>sourceIn / factor</c> mapping, which assumed a stretcher places source second s at output second
    /// s/factor — true of the geometry, not of a WSOLA pass with its own latency.</para>
    /// </summary>
    internal StereoBuffer DecodeRampedStereo(
        string path,
        int sampleRate,
        double sourceInSeconds,
        Func<double, double> tempoPercentAt,
        int maxFrames = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(tempoPercentAt);

        if (!BassAudioDecoder.EnsureUsable())
        {
            _log?.LogWarning(
                "STUDIO ramp: BASS is unavailable, so '{Path}' cannot be time-stretched and would render " +
                "as silence.", path);
            return Empty();
        }

        int decode = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float);
        if (decode == 0)
        {
            _log?.LogWarning("STUDIO ramp: BASS CreateStream('{Path}') failed: {Error}.", path, Bass.LastError);
            return Empty();
        }

        // Seek the RAW stream: the tempo stream has not been created yet, so this positions the material
        // that will be fed to it rather than a position inside stretched output.
        if (sourceInSeconds > 0.0)
        {
            long bytes = Bass.ChannelSeconds2Bytes(decode, sourceInSeconds);
            if (bytes < 0 || !Bass.ChannelSetPosition(decode, bytes))
            {
                _log?.LogWarning(
                    "STUDIO ramp: could not seek '{Path}' to {Seconds:F3}s ({Error}); the clip would enter " +
                    "at the wrong point, so it is dropped from the mix.", path, sourceInSeconds, Bass.LastError);
                Bass.StreamFree(decode);
                return Empty();
            }
        }

        int tempo = BassFx.TempoCreate(decode, BassFlags.Decode | BassFlags.FxFreeSource);
        if (tempo == 0)
        {
            _log?.LogWarning("STUDIO ramp: BASS_FX TempoCreate failed: {Error}.", Bass.LastError);
            Bass.StreamFree(decode);
            return Empty();
        }

        int mixer = BassMix.CreateMixerStream(sampleRate, RenderChannels, BassFlags.Decode | BassFlags.Float);
        if (mixer == 0)
        {
            _log?.LogWarning("STUDIO ramp: CreateMixerStream failed: {Error}.", Bass.LastError);
            Bass.StreamFree(tempo);
            return Empty();
        }

        if (!BassMix.MixerAddChannel(mixer, tempo, BassFlags.Default))
        {
            _log?.LogWarning("STUDIO ramp: MixerAddChannel failed: {Error}.", Bass.LastError);
            Bass.StreamFree(mixer);
            Bass.StreamFree(tempo);
            return Empty();
        }

        long maxFloats = maxFrames >= int.MaxValue / RenderChannels
            ? long.MaxValue
            : (long)(maxFrames + PullFloats) * RenderChannels;
        var interleaved = new List<float>();
        var buffer = new float[PullFloats];
        while (interleaved.Count < maxFloats)
        {
            // Set the rate for the block about to be produced, from the output position it starts at.
            double outputSeconds = interleaved.Count / (double)(RenderChannels * sampleRate);
            Bass.ChannelSetAttribute(tempo, ChannelAttribute.Tempo, (float)tempoPercentAt(outputSeconds));

            int bytes = Bass.ChannelGetData(mixer, buffer, PullFloats * sizeof(float));
            if (bytes <= 0)
                break;
            int got = bytes / sizeof(float);
            for (int i = 0; i < got; i++)
                interleaved.Add(buffer[i]);
        }

        Bass.StreamFree(mixer);
        Bass.StreamFree(tempo);

        // One correction for the clip, taken at the tempo it spends most of its life at: across a set's
        // tempo range the latency moves by about 3 ms, so sampling it mid-clip leaves well under a
        // millisecond — far below what a listener hears as a flam.
        double midpointSeconds = interleaved.Count / (double)(RenderChannels * sampleRate) / 2.0;
        return Realign(
            Deinterleave(interleaved),
            LatencySeconds(tempoPercentAt(midpointSeconds), sampleRate),
            sampleRate);
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
