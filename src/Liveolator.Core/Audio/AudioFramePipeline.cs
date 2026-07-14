using Liveolator.Core.Dsp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Core.Audio;

/// <summary>
/// Orchestrates the audio frame pipeline (doc 02): subscribes to an <see cref="IAudioSource"/>,
/// downmixes interleaved samples to mono, optionally resamples to a fixed analysis rate, slices the
/// result into overlapping analysis frames, and emits an immutable <see cref="AudioFrameData"/> per
/// frame via <see cref="SpectrumAnalyzer"/>. Pure and deterministic — fed samples, it produces
/// frames — so it unit-tests with a fake source.
/// </summary>
/// <remarks>
/// <para>
/// Resampling is <b>opt-in and back-compatible</b>. With no <c>analysisSampleRate</c> the pipeline
/// analyses at the source's native rate and carries that rate on every <see cref="AudioFrameData"/>
/// — the original behaviour, unchanged. When an analysis rate is supplied the downmixed mono is
/// resampled (linear, streaming-continuous) from the source rate to that rate <i>before</i> framing,
/// so tempo analysis is consistent across 44.1/48/96 kHz sources; <see cref="AudioFrameData.SampleRate"/>
/// then reports the analysis rate and frame index/timestamp continuity is tracked in resampled time.
/// </para>
/// <para>
/// Because timestamps remain monotonic and spaced by hop/rate in either mode, <c>AudioBeatClock</c>'s
/// envelope-rate derivation (which reads frame timestamps) is unaffected.
/// </para>
/// </remarks>
public sealed class AudioFramePipeline : IAudioFrameProvider, IDisposable
{
    private readonly IAudioSource _source;
    private readonly SpectrumAnalyzer _analyzer;
    private readonly int _frameSize;
    private readonly int _hop;
    private readonly int? _analysisSampleRate; // null = analyse at the source's native rate
    private readonly ILogger _logger;

    private readonly object _gate = new();
    private readonly List<float> _mono = new();
    private LinearResampler? _resampler; // created/recreated for the live source rate when resampling
    private int _consumed;          // mono samples already used as frame starts, from _mono[0]
    private long _absoluteStart;    // absolute index of _mono[0] in the (analysis-rate) stream
    private long _frameIndex;
    private int _sampleRate;        // the rate stamped on emitted frames (analysis rate when resampling)
    private int _lastChannels = -1; // for log-once on format change
    private int _lastRate = -1;
    private bool _disposed;

    private volatile AudioFrameData _latest = AudioFrameData.Empty;

    public AudioFramePipeline(
        IAudioSource source,
        SpectrumAnalyzer analyzer,
        int hop = 512,
        ILogger<AudioFramePipeline>? logger = null,
        int? analysisSampleRate = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _frameSize = analyzer.FrameSize;
        if (hop < 1 || hop > _frameSize)
            throw new ArgumentOutOfRangeException(nameof(hop), "hop must be in [1, frameSize].");
        if (analysisSampleRate is <= 0)
            throw new ArgumentOutOfRangeException(nameof(analysisSampleRate), "Analysis sample rate must be positive.");

        _hop = hop;
        _analysisSampleRate = analysisSampleRate;
        _logger = logger ?? NullLogger<AudioFramePipeline>.Instance;
        _source.SamplesAvailable += OnSamplesAvailable;
    }

    public event EventHandler<AudioFrameData>? FrameAvailable;

    public AudioFrameData GetLatestFrame() => _latest;

    private void OnSamplesAvailable(object? sender, AudioSamplesAvailable e)
    {
        // Bad-frame guard (doc 02): never throw into the capture thread; skip and keep the last
        // valid frame as the latest snapshot.
        if (e.Channels < 1 || e.SampleRate < 1 || e.Interleaved.IsEmpty)
            return;

        List<AudioFrameData> emitted;
        lock (_gate)
        {
            if (e.Channels != _lastChannels || e.SampleRate != _lastRate)
            {
                // Log-once per format change to avoid per-frame log spam (doc 02).
                _logger.LogInformation(
                    "Audio frame pipeline format: {Channels} ch @ {SampleRate} Hz", e.Channels, e.SampleRate);
                _lastChannels = e.Channels;
                _lastRate = e.SampleRate;
                // (Re)build the resampler for the live source rate; framing time stays continuous
                // because _absoluteStart/_frameIndex are never reset across the change.
                _resampler = _analysisSampleRate is int target
                    ? new LinearResampler(e.SampleRate, target)
                    : null;
            }
            // Frames are stamped with the analysis rate when resampling, else the source rate.
            _sampleRate = _analysisSampleRate ?? e.SampleRate;

            AppendAnalysisMono(e.Interleaved.Span, e.Channels);
            emitted = DrainFrames();
        }

        if (emitted.Count == 0)
            return;

        _latest = emitted[^1];
        var handler = FrameAvailable;
        if (handler is null)
            return;
        foreach (var frame in emitted)
            handler(this, frame);
    }

    /// <summary>
    /// Downmix the interleaved batch to mono, then (when an analysis rate is configured) resample it
    /// to that rate, appending the result to the analysis buffer. Resampling happens after downmix so
    /// it runs once per mono sample rather than per channel.
    /// </summary>
    private void AppendAnalysisMono(ReadOnlySpan<float> interleaved, int channels)
    {
        int frames = interleaved.Length / channels; // ignore a trailing partial frame
        if (frames == 0)
            return;

        if (_resampler is null)
        {
            for (int f = 0; f < frames; f++)
                _mono.Add(DownmixFrame(interleaved, f, channels));
            return;
        }

        // Resampling needs the whole batch at once to preserve streaming phase continuity.
        var mono = new float[frames];
        for (int f = 0; f < frames; f++)
            mono[f] = DownmixFrame(interleaved, f, channels);
        _mono.AddRange(_resampler.Process(mono));
    }

    /// <summary>Average the channels of one interleaved frame into a single mono sample.</summary>
    private static float DownmixFrame(ReadOnlySpan<float> interleaved, int frame, int channels)
    {
        int baseIdx = frame * channels;
        float sum = 0f;
        for (int c = 0; c < channels; c++)
            sum += interleaved[baseIdx + c];
        return sum / channels;
    }

    /// <summary>Emit every fully-available overlapping frame, then compact the buffer.</summary>
    private List<AudioFrameData> DrainFrames()
    {
        var emitted = new List<AudioFrameData>();
        while (_consumed + _frameSize <= _mono.Count)
        {
            ReadOnlySpan<float> frame = System.Runtime.InteropServices.CollectionsMarshal
                .AsSpan(_mono).Slice(_consumed, _frameSize);

            var (spectrum, waveform) = _analyzer.Analyze(frame);
            long frameStart = _absoluteStart + _consumed;

            emitted.Add(new AudioFrameData(
                MonoPcm: frame.ToArray(),
                Spectrum: spectrum,
                Waveform: waveform,
                SampleRate: _sampleRate,
                FrameIndex: _frameIndex++,
                TimestampSeconds: (double)frameStart / _sampleRate));

            _consumed += _hop;
        }

        // Drop fully-consumed prefix so the buffer doesn't grow unbounded.
        if (_consumed >= _frameSize)
        {
            _mono.RemoveRange(0, _consumed);
            _absoluteStart += _consumed;
            _consumed = 0;
        }

        return emitted;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _source.SamplesAvailable -= OnSamplesAvailable;
    }
}
