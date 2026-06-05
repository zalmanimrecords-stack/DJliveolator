using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Core.Audio;

/// <summary>
/// Orchestrates the audio frame pipeline (doc 02): subscribes to an <see cref="IAudioSource"/>,
/// downmixes interleaved samples to mono, slices them into overlapping analysis frames, and emits
/// an immutable <see cref="AudioFrameData"/> per frame via <see cref="SpectrumAnalyzer"/>. Pure
/// and deterministic — fed samples, it produces frames — so it unit-tests with a fake source.
/// </summary>
/// <remarks>
/// Resampling is intentionally out of scope here: analysis runs at the source's native rate and
/// that rate is carried on every <see cref="AudioFrameData"/>, so downstream consumers (the beat
/// engine) stay rate-aware. A fixed-rate analysis path can be layered on later without changing
/// this seam.
/// </remarks>
public sealed class AudioFramePipeline : IAudioFrameProvider, IDisposable
{
    private readonly IAudioSource _source;
    private readonly SpectrumAnalyzer _analyzer;
    private readonly int _frameSize;
    private readonly int _hop;
    private readonly ILogger _logger;

    private readonly object _gate = new();
    private readonly List<float> _mono = new();
    private int _consumed;          // mono samples already used as frame starts, from _mono[0]
    private long _absoluteStart;    // absolute index of _mono[0] in the whole stream
    private long _frameIndex;
    private int _sampleRate;
    private int _lastChannels = -1; // for log-once on format change
    private int _lastRate = -1;
    private bool _disposed;

    private volatile AudioFrameData _latest = AudioFrameData.Empty;

    public AudioFramePipeline(
        IAudioSource source,
        SpectrumAnalyzer analyzer,
        int hop = 512,
        ILogger<AudioFramePipeline>? logger = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _frameSize = analyzer.FrameSize;
        if (hop < 1 || hop > _frameSize)
            throw new ArgumentOutOfRangeException(nameof(hop), "hop must be in [1, frameSize].");

        _hop = hop;
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
            }
            _sampleRate = e.SampleRate;

            AppendDownmixedMono(e.Interleaved.Span, e.Channels);
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

    /// <summary>Average the channels of each interleaved frame into one mono sample.</summary>
    private void AppendDownmixedMono(ReadOnlySpan<float> interleaved, int channels)
    {
        int frames = interleaved.Length / channels; // ignore a trailing partial frame
        for (int f = 0; f < frames; f++)
        {
            int baseIdx = f * channels;
            float sum = 0f;
            for (int c = 0; c < channels; c++)
                sum += interleaved[baseIdx + c];
            _mono.Add(sum / channels);
        }
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
