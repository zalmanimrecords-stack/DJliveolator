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

    // Grace period before a decoded source is released, absorbing the rounding between timeline seconds and
    // buffer frames at a block boundary. One second costs a few MB and removes the whole class of
    // "released one block too early" bugs.
    private const double ReleaseMarginSeconds = 1.0;

    // Below this on both channels a frame counts as silence for MixRenderResult. -80 dBFS: far under
    // anything audible, far over float/limiter residue, and under the 16-bit LSB (-90 dBFS) that a
    // digitally silent export measures at.
    private const float SilenceFloor = 1e-4f;

    private readonly IAudioDecoder _decoder;
    private readonly BassFxRenderDecoder _stretchDecoder;
    private readonly ILogger? _log;

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
        _log = logger;
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
    /// <para>Streams: each block is mixed, limited and written straight to disk, and a decoded source is
    /// dropped once the timeline has passed the last clip that reads it. Nothing proportional to the whole
    /// arrangement is ever held in memory, so an hour-long set renders in roughly the footprint of the two
    /// or three tracks actually sounding — and the old ~2 GB single-array ceiling (about 101 minutes at
    /// 44.1 kHz without <c>gcAllowVeryLargeObjects</c>) is gone.</para>
    /// <para>Returns what was actually produced. A source that fails to decode still degrades to silence
    /// rather than aborting the render (global #16/#26), but it is now reported in
    /// <see cref="MixRenderResult.SilentSources"/> instead of only to a log the caller may not have wired
    /// up — so a mix with silent stretches cannot be mistaken for a finished one.</para>
    /// </summary>
    public async Task<MixRenderResult> RenderAsync(
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
        long totalFrames = plan.DurationSeconds > 0 ? (long)Math.Ceiling(plan.DurationSeconds * sampleRate) : 0;

        // The furthest source position (seconds) any clip needs for each (path, factor): a clip bounded by
        // its out-point only needs up to SourceOut; an open-ended clip needs the whole file. Decoding only
        // this far keeps render memory proportional to the material used, not the source file lengths.
        Dictionary<string, double> sourceEndSeconds = MaxSourceEndPerKey(project, plan);

        // The last timeline second each decoded buffer is read at, so it can be released the moment the
        // playhead is past it. Without this the sources dictionary alone holds every track at once and
        // streaming the output would not have bounded the render.
        Dictionary<string, double> sourceLastNeeded = LastNeededSecondPerKey(project, plan);

        // Decoded lazily on first use rather than all up front, so only the sources currently sounding
        // (plus any not yet evicted) are resident.
        var sources = new Dictionary<string, StereoBuffer>(StringComparer.OrdinalIgnoreCase);

        // What the render produced, for MixRenderResult. `decoded` also stops a source that was released
        // and re-decoded from being counted (or reported silent) twice.
        var decoded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var silentSources = new List<string>();
        var monoFallbackSources = new List<string>();
        long silentFrames = 0;
        var holeScan = new HoleScan(sampleRate);

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

        // One limiter instance across the whole render: it carries its look-ahead delay line and gain state
        // between calls, so limiting block by block is identical to one pass over the finished master.
        var limiter = new MasterLimiter(sampleRate, channels: OutputChannels);
        int latencyFrames = limiter.LatencySamples;

        // The limiter delays by its look-ahead, so output frame j carries input frame j - latency. To emit
        // input [0, totalFrames) we push one extra look-ahead window of silence through and drop the first
        // `latency` output frames — that is what keeps the file sample-aligned to the timeline.
        long flushFrames = totalFrames > 0 ? latencyFrames : 0;
        long inputFrames = totalFrames + flushFrames;

        // Interleaved stereo block (L0,R0,L1,R1,...) so the stereo-linked limiter sees both channels.
        var block = new float[BlockSize * OutputChannels];
        long emitted = 0;   // limiter output frames seen so far, including the primed ones we discard
        long written = 0;   // frames actually written to the file

        using var writer = new WavStreamWriter(outputPath, OutputChannels, sampleRate);

        for (long blockStart = 0; blockStart < inputFrames; blockStart += BlockSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int blockLen = (int)Math.Min(BlockSize, inputFrames - blockStart);
            int blockFloats = blockLen * OutputChannels;
            Array.Clear(block, 0, blockFloats);
            double tBlock = blockStart / (double)sampleRate;

            // Past the end of the arrangement there is nothing to mix; the silence flushes the look-ahead.
            if (blockStart < totalFrames)
            {
                for (int slot = 0; slot < decks; slot++)
                {
                    DeckMixState state = plan.EvaluateDeck(slot, tBlock);
                    if (!state.HasAudio || state.SourcePath is null)
                    {
                        // Deck is silent this block: its next sounding clip is a genuine source discontinuity,
                        // so drop the persistent state (a fresh stream would start at zero).
                        deckSource[slot] = null;
                        continue;
                    }

                    string key = SourceKey(state.SourcePath, state.WarpFactor);
                    if (!sources.TryGetValue(key, out StereoBuffer? src))
                    {
                        // Only decode material the plan actually accounted for; an unplanned key would have
                        // no decode bound, and the old code treated it as silence.
                        if (!sourceEndSeconds.TryGetValue(key, out double endSeconds))
                        {
                            deckSource[slot] = null;
                            continue;
                        }

                        (StereoBuffer decodedSource, bool tookMonoFallback) = await DecodeSourceAsync(
                                state.SourcePath, state.WarpFactor, sampleRate, endSeconds, cancellationToken)
                            .ConfigureAwait(false);
                        src = decodedSource;
                        sources[key] = src;

                        if (tookMonoFallback &&
                            !monoFallbackSources.Contains(state.SourcePath, StringComparer.OrdinalIgnoreCase))
                        {
                            monoFallbackSources.Add(state.SourcePath);
                        }

                        // A decode that came back with nothing is a clip the mix will not contain. It is
                        // deliberately not fatal here, but it must reach the caller.
                        if (decoded.Add(key) && src.Length == 0 &&
                            !silentSources.Contains(state.SourcePath, StringComparer.OrdinalIgnoreCase))
                        {
                            silentSources.Add(state.SourcePath);
                        }
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
                    long srcStart = (long)Math.Round(bufferSeconds * sampleRate);
                    for (int i = 0; i < blockLen; i++)
                    {
                        long si = srcStart + i;
                        bool inRange = si >= 0 && si < src.Length;

                        // Process each channel through its own delay line: filter(high(mid(low(x)))) per L/R.
                        double l = (inRange ? src.Left[si] : 0.0) * state.Gain;
                        l = filt[slot].Process(Left, high[slot].Process(Left, mid[slot].Process(Left, low[slot].Process(Left, l))));

                        double r = (inRange ? src.Right[si] : 0.0) * state.Gain;
                        r = filt[slot].Process(Right, high[slot].Process(Right, mid[slot].Process(Right, low[slot].Process(Right, r))));

                        int frame = i * OutputChannels;
                        block[frame + Left] += (float)l;
                        block[frame + Right] += (float)r;
                    }
                }
            }

            limiter.Process(block.AsSpan(0, blockFloats));

            // Discard the limiter's primed frames, then keep only as many as the timeline actually has.
            int skip = (int)Math.Min(blockLen, Math.Max(0L, latencyFrames - emitted));
            int keep = (int)Math.Min(blockLen - skip, totalFrames - written);
            if (keep > 0)
            {
                int outputStart = skip * OutputChannels;
                int outputFloats = keep * OutputChannels;
                writer.Write(block.AsSpan(outputStart, outputFloats));
                written += keep;
                silentFrames += CountSilentFrames(block, outputStart, outputFloats);
                holeScan.Add(block, outputStart, outputFloats);
            }
            emitted += blockLen;

            ReleasePassedSources(sources, sourceLastNeeded, tBlock);
            progress?.Report(totalFrames == 0 ? 1.0 : Math.Min(1.0, written / (double)totalFrames));
        }

        progress?.Report(1.0);
        return new MixRenderResult(
            decoded.Count, silentSources, written, silentFrames, holeScan.Finish(), monoFallbackSources);
    }

    /// <summary>
    /// Per-window level of the written master, accumulated on the blocks already in hand, reporting every
    /// run of windows under the floor.
    /// <para>Whole-file loudness cannot verify a render: a mix that was ~95% digital silence measured a
    /// healthy -10.3 LUFS, because the few sounding clips carried the average. Neither can
    /// <see cref="MixRenderResult.SilentFraction"/> on its own — the 2026-08-13 export shipped a 10.5 s hole
    /// bottoming at -63.7 dB, which is 0.2% of a 70-minute set. Only windows find it, and only while the
    /// samples are passing through: a second pass over a 700 MB WAV is not free.</para>
    /// </summary>
    private sealed class HoleScan
    {
        // The two tunables. 2 s so that normal music cannot dip a window under the floor: at 145 BPM a bar
        // is 1.66 s, so a window always spans a full bar plus — a kick gap, a snare-less beat or a one-bar
        // stop averages back up. Halve it and every breakbeat gap reads as a hole. -40 dBFS RMS sits ~30 dB
        // under a mastered psy mix, so only a genuine withdrawal reaches it, while the measured holes
        // (-63.7 dB) clear it by a wide margin.
        private const double WindowSeconds = 2.0;
        private const double FloorDbfs = -40.0;

        // Keeps a digitally silent window's level finite (-140 dBFS) so the report carries a number rather
        // than -infinity, which serialises badly and reads as "unmeasured".
        private const double MinRms = 1e-7;

        private readonly List<MixHole> _holes = new();
        private readonly int _sampleRate;
        private readonly int _windowFrames;

        private double _sumSquares;
        private int _windowFilled;
        private long _windowStartFrame;

        // The run of consecutive under-floor windows currently open, if any.
        private long _runStartFrame = -1;
        private long _runEndFrame;
        private double _runDeepestDb;

        internal HoleScan(int sampleRate)
        {
            _sampleRate = sampleRate;
            _windowFrames = Math.Max(1, (int)Math.Round(WindowSeconds * sampleRate));
        }

        // Interleaved stereo, same layout as the render block. Takes the array rather than a span because
        // the caller is an async method, where a ref-struct local is not allowed at this language version.
        internal void Add(float[] interleaved, int start, int floats)
        {
            for (int i = start; i + Right < start + floats; i += OutputChannels)
            {
                double l = interleaved[i + Left];
                double r = interleaved[i + Right];
                _sumSquares += (l * l) + (r * r);
                if (++_windowFilled == _windowFrames)
                    CloseWindow();
            }
        }

        internal IReadOnlyList<MixHole> Finish()
        {
            // A trailing part-window is only judged once it holds at least half a window: a mix's last
            // fraction of a second is routinely a decayed tail, and calling that a hole would put a false
            // positive on the end of every clean render.
            if (_windowFilled * 2 >= _windowFrames)
                CloseWindow();

            CloseRun();
            return _holes;
        }

        private void CloseWindow()
        {
            double rms = Math.Sqrt(_sumSquares / (_windowFilled * (double)OutputChannels));
            double db = 20.0 * Math.Log10(Math.Max(rms, MinRms));
            long end = _windowStartFrame + _windowFilled;

            if (db < FloorDbfs)
            {
                if (_runStartFrame < 0)
                {
                    _runStartFrame = _windowStartFrame;
                    _runDeepestDb = db;
                }
                else
                {
                    _runDeepestDb = Math.Min(_runDeepestDb, db);
                }

                _runEndFrame = end;
            }
            else
            {
                CloseRun();
            }

            _windowStartFrame = end;
            _windowFilled = 0;
            _sumSquares = 0;
        }

        private void CloseRun()
        {
            if (_runStartFrame < 0)
                return;

            _holes.Add(new MixHole(
                _runStartFrame / (double)_sampleRate,
                (_runEndFrame - _runStartFrame) / (double)_sampleRate,
                Math.Round(_runDeepestDb, 1)));
            _runStartFrame = -1;
        }
    }

    // Frames with both channels under the silence floor, counted on the block already in hand so the
    // finished file never has to be read back. Takes the array rather than a span because this runs inside
    // an async method, where a ref-struct local is not allowed at the project's language version.
    private static long CountSilentFrames(float[] interleaved, int start, int floats)
    {
        long silent = 0;
        for (int i = start; i + Right < start + floats; i += OutputChannels)
        {
            if (Math.Abs(interleaved[i + Left]) < SilenceFloor && Math.Abs(interleaved[i + Right]) < SilenceFloor)
                silent++;
        }

        return silent;
    }

    // The last timeline second each (path, factor) buffer is read at: the clip's start plus its own
    // timeline length (source span divided by the warp factor, since a warped buffer plays 1:1 with the
    // timeline). An open-ended clip has no known end, so its buffer is never released.
    private static Dictionary<string, double> LastNeededSecondPerKey(StudioProject project, MixPlan plan)
    {
        var lastNeeded = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (StudioClip clip in project.Clips)
        {
            double factor = plan.WarpFactorFor(clip);
            string key = SourceKey(clip.TrackPath, factor);

            double end = double.PositiveInfinity;
            if (clip.SourceOut is { } sourceOut && factor > 0)
            {
                double sourceSpan = sourceOut.TotalSeconds - clip.SourceIn.TotalSeconds;
                if (sourceSpan >= 0)
                    end = clip.TimelineStartSeconds + (sourceSpan / factor);
            }

            lastNeeded[key] = lastNeeded.TryGetValue(key, out double existing) ? Math.Max(existing, end) : end;
        }

        return lastNeeded;
    }

    // Release buffers the playhead is past. The margin covers the rounding between timeline seconds and
    // buffer frames at a block boundary, so a buffer is never dropped while its clip's last samples are
    // still being read.
    private static void ReleasePassedSources(
        Dictionary<string, StereoBuffer> sources, Dictionary<string, double> lastNeeded, double tBlock)
    {
        if (sources.Count == 0)
            return;

        List<string>? stale = null;
        foreach (string key in sources.Keys)
        {
            if (lastNeeded.TryGetValue(key, out double end) && tBlock > end + ReleaseMarginSeconds)
                (stale ??= new List<string>()).Add(key);
        }

        if (stale is null)
            return;
        foreach (string key in stale)
            sources.Remove(key);
    }

    // The furthest source position (seconds) any clip needs per (path, factor). A clip with a known
    // out-point needs up to SourceOut; an open-ended clip (null out) needs the whole file, recorded as
    // PositiveInfinity so the decode is not capped. Shared buffers take the maximum across their clips.
    private static Dictionary<string, double> MaxSourceEndPerKey(StudioProject project, MixPlan plan)
    {
        var needed = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (StudioClip clip in project.Clips)
        {
            string key = SourceKey(clip.TrackPath, plan.WarpFactorFor(clip));
            double end = clip.SourceOut?.TotalSeconds ?? double.PositiveInfinity;
            needed[key] = needed.TryGetValue(key, out double existing) ? Math.Max(existing, end) : end;
        }

        return needed;
    }

    // Decode one (path, warp factor) to a stereo buffer at the render rate, up to maxSourceEndSeconds of
    // source (PositiveInfinity = the whole file). Unwarped: the managed mono decoder duplicated to both
    // channels (CI-safe, deterministic, no native). Warped: BASS_FX stereo. A test decode override (when
    // present) supplies the buffer directly so distinct L/R can be injected.
    // The flag says whether the MONO fallback was taken, which the caller reports: a mono clip inside a
    // stereo mix is a defect the render must not keep to itself (see MixRenderResult.MonoFallbackSources).
    private async Task<(StereoBuffer Buffer, bool MonoFallback)> DecodeSourceAsync(
        string path, double factor, int sampleRate, double maxSourceEndSeconds, CancellationToken cancellationToken)
    {
        if (_decodeOverride is not null)
            return (_decodeOverride(path, factor), false);

        // The decoded buffer plays 1:1 with the timeline, so source second s maps to buffer second s/factor.
        // The furthest buffer frame any clip reads is therefore maxSourceEnd/factor (+ a block of margin for
        // rounding at the boundary). Infinite ⇒ no cap.
        int maxFrames = int.MaxValue;
        if (!double.IsPositiveInfinity(maxSourceEndSeconds) && factor > 0)
        {
            double bufferSeconds = maxSourceEndSeconds / factor;
            double frames = (bufferSeconds * sampleRate) + (2 * BlockSize);
            maxFrames = frames >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(frames);
        }

        // Every clip decodes through the native stereo path, warped or not: routing unwarped clips to the
        // analysis decoder instead (which is mono by seam contract) silently collapsed any track already at
        // the project tempo to L=R — eleven minutes of a measured 68-minute export came out in mono.
        bool unwarped = Math.Abs(factor - 1.0) < UnwarpedEpsilon;
        StereoBuffer stereo = _stretchDecoder.DecodeStretchedStereo(
            path, sampleRate, unwarped ? 0.0 : (factor - 1.0) * 100.0, maxFrames);
        if (stereo.Length > 0 || !unwarped)
            return (stereo, false);

        // Nothing from BASS (absent native, or a format it cannot open). For an unwarped clip the managed
        // decoder is an equivalent second attempt apart from channel count, so the mix still renders — in
        // mono. A WARPED clip must never fall back here: unstretched audio would play at the wrong tempo,
        // so its empty buffer stands and RenderAsync reports it as a silent source.
        // Say so twice over: the log for whoever is watching the render, and the returned flag for the caller,
        // because a warning nobody had wired up is how eleven minutes of a 68-minute export shipped in mono.
        _log?.LogWarning(
            "STUDIO render: BASS could not decode '{Path}', falling back to the managed mono decoder — this " +
            "clip renders in MONO (no stereo image) inside a stereo mix.", path);
        StereoBuffer mono = StereoBuffer.FromMono(
            await DecodeMonoAsync(path, sampleRate, maxFrames, cancellationToken).ConfigureAwait(false));
        // Only a fallback that produced audio is a mono clip. When it produced none the clip is not in the
        // mix at all, and SilentSources is the honest report — blaming the channel count would misdirect.
        return (mono, mono.Length > 0);
    }

    private static StatefulBiquad[] NewBiquads(int count)
    {
        var biquads = new StatefulBiquad[count];
        for (int i = 0; i < count; i++)
            biquads[i] = new StatefulBiquad(channels: OutputChannels);
        return biquads;
    }

    // Decode mono PCM up to maxFrames samples (int.MaxValue = the whole file), stopping enumeration early
    // once enough is buffered so a trimmed clip on a long track doesn't materialise the entire file.
    private async Task<float[]> DecodeMonoAsync(string path, int sampleRate, int maxFrames, CancellationToken cancellationToken)
    {
        var samples = new List<float>();
        await foreach (ReadOnlyMemory<float> block in _decoder.DecodeMonoAsync(path, sampleRate, cancellationToken)
            .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int i = 0; i < block.Length; i++)
                samples.Add(block.Span[i]);
            if (samples.Count >= maxFrames)
                break;
        }

        return samples.ToArray();
    }
}
