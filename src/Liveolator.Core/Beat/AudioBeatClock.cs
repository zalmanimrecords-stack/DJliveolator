using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Audio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Core.Beat;

/// <summary>
/// The realtime half of the beat engine (doc 03): turns the shared audio frames (doc 02) into a
/// live <see cref="BeatClockState"/>. Per frame it accumulates a spectral-flux onset envelope;
/// periodically it runs autocorrelation tempo estimation (<see cref="TempoEstimator"/>) and, once
/// a confident tempo is found, anchors a <see cref="BeatTimeline"/> to host time so beat/bar phase
/// flows smoothly. The same clock can later drive both the DJ mix and visuals (doc 00/03).
/// </summary>
/// <remarks>
/// Time is injected via <see cref="IHostClock"/> and audio arrives via <see cref="IAudioFrameProvider"/>,
/// so the whole service is deterministic and unit-tests without hardware. Lock/nudge performer
/// controls are intentionally out of scope here — this clock follows the audio.
/// </remarks>
public sealed class AudioBeatClock : IBeatClock, IDisposable
{
    private readonly IAudioFrameProvider _frames;
    private readonly IHostClock _hostClock;
    private readonly TempoEstimator _tempo;
    private readonly int _beatsPerBar;
    private readonly BeatClockSource _source;
    private readonly double _confidenceThreshold;
    private readonly double _analysisWindowSeconds;
    private readonly double _minWindowSeconds;
    private readonly double _estimateIntervalSeconds;
    private readonly double _retuneTolerance;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    private readonly Queue<double> _flux = new();
    private float[]? _prevSpectrum;
    private double _lastTimestamp = double.NaN;
    private double _envelopeRateHz;
    private int _framesSinceEstimate;

    private BeatTimeline? _timeline;
    private double _bpm;
    private double _confidence;
    private IReadOnlyList<TempoCandidate> _candidates = Array.Empty<TempoCandidate>();
    private int _lastBeatIndex = int.MinValue;
    private int _lastBarIndex = int.MinValue;
    private bool _disposed;

    public AudioBeatClock(
        IAudioFrameProvider frames,
        IHostClock hostClock,
        TempoEstimator? tempoEstimator = null,
        int beatsPerBar = BeatQuantizer.DefaultBeatsPerBar,
        BeatClockSource source = BeatClockSource.System,
        double confidenceThreshold = 0.02,
        double analysisWindowSeconds = 6.0,
        double minWindowSeconds = 1.5,
        double estimateIntervalSeconds = 0.35,
        double retuneTolerance = 0.02,
        ILogger<AudioBeatClock>? logger = null)
    {
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        _hostClock = hostClock ?? throw new ArgumentNullException(nameof(hostClock));
        if (beatsPerBar <= 0)
            throw new ArgumentOutOfRangeException(nameof(beatsPerBar), "Beats per bar must be positive.");
        if (minWindowSeconds <= 0 || analysisWindowSeconds < minWindowSeconds)
            throw new ArgumentException("Require 0 < minWindowSeconds <= analysisWindowSeconds.");

        _tempo = tempoEstimator ?? new TempoEstimator();
        _beatsPerBar = beatsPerBar;
        _source = source;
        _confidenceThreshold = confidenceThreshold;
        _analysisWindowSeconds = analysisWindowSeconds;
        _minWindowSeconds = minWindowSeconds;
        _estimateIntervalSeconds = estimateIntervalSeconds;
        _retuneTolerance = retuneTolerance;
        _logger = logger ?? NullLogger<AudioBeatClock>.Instance;

        Current = BeatClockState.Idle;
        _frames.FrameAvailable += OnFrame;
    }

    /// <inheritdoc />
    public BeatClockState Current { get; private set; }

    /// <inheritdoc />
    public event EventHandler<BeatClockState>? StateChanged;

    /// <summary>The current detected tempo, or 0 before a confident lock.</summary>
    public double Bpm { get { lock (_gate) return _bpm; } }

    /// <summary>Advance phase at an arbitrary host time (render-loop entry point); republishes if locked.</summary>
    public void Update(long hostTimeTicks)
    {
        BeatClockState? published = null;
        lock (_gate)
        {
            if (_timeline is not null)
                published = Current = BuildState(hostTimeTicks);
        }
        if (published is not null)
            StateChanged?.Invoke(this, published);
    }

    private void OnFrame(object? sender, AudioFrameData frame)
    {
        if (_disposed || frame.FrameIndex < 0 || frame.Spectrum.Length == 0 || frame.SampleRate <= 0)
            return;

        BeatClockState? published = null;
        lock (_gate)
        {
            UpdateEnvelopeRate(frame.TimestampSeconds);
            AccumulateFlux(frame.Spectrum);

            _framesSinceEstimate++;
            if (ShouldEstimate())
            {
                _framesSinceEstimate = 0;
                RunEstimate(_hostClock.NowTicks);
            }

            if (_timeline is not null)
                published = Current = BuildState(_hostClock.NowTicks);
        }

        if (published is not null)
            StateChanged?.Invoke(this, published);
    }

    private void UpdateEnvelopeRate(double timestampSeconds)
    {
        if (!double.IsNaN(_lastTimestamp))
        {
            double dt = timestampSeconds - _lastTimestamp;
            if (dt > 0.0)
                _envelopeRateHz = 1.0 / dt;
        }
        _lastTimestamp = timestampSeconds;
    }

    private void AccumulateFlux(float[] spectrum)
    {
        if (_prevSpectrum is not null && _envelopeRateHz > 0.0)
        {
            _flux.Enqueue(SpectralFlux.Positive(_prevSpectrum, spectrum));
            int maxCount = Math.Max(8, (int)(_analysisWindowSeconds * _envelopeRateHz));
            while (_flux.Count > maxCount)
                _flux.Dequeue();
        }
        _prevSpectrum = spectrum;
    }

    private bool ShouldEstimate()
    {
        if (_envelopeRateHz <= 0.0)
            return false;
        int interval = Math.Max(1, (int)(_estimateIntervalSeconds * _envelopeRateHz));
        int minCount = Math.Max(8, (int)(_minWindowSeconds * _envelopeRateHz));
        return _framesSinceEstimate >= interval && _flux.Count >= minCount;
    }

    private void RunEstimate(long nowTicks)
    {
        TempoEstimate est = _tempo.Estimate(_flux.ToArray(), _envelopeRateHz);
        if (est.Bpm <= 0.0 || est.Confidence < _confidenceThreshold)
            return;

        _confidence = est.Confidence;
        _candidates = new TempoCandidate[]
        {
            new(est.Bpm, est.Confidence),
            new(est.Bpm / 2.0, est.Confidence * 0.5),
            new(est.Bpm * 2.0, est.Confidence * 0.5),
        };

        if (_timeline is null)
        {
            _bpm = est.Bpm;
            _timeline = new BeatTimeline(_bpm, anchorBeat: 0, nowTicks, _hostClock.TicksPerSecond);
            _lastBeatIndex = int.MinValue;
            _lastBarIndex = int.MinValue;
            _logger.LogInformation("Beat lock: {Bpm:F1} BPM (confidence {Confidence:F2})", _bpm, est.Confidence);
        }
        else if (Math.Abs(est.Bpm - _bpm) / _bpm > _retuneTolerance)
        {
            // Re-anchor at the current beat so the grid stays continuous across a tempo change.
            double currentBeat = _timeline.BeatAtTime(nowTicks);
            _bpm = est.Bpm;
            _timeline = new BeatTimeline(_bpm, currentBeat, nowTicks, _hostClock.TicksPerSecond);
        }
    }

    private BeatClockState BuildState(long nowTicks)
    {
        double beat = _timeline!.BeatAtTime(nowTicks);
        int beatIndex = (int)Math.Floor(beat);
        int barIndex = (int)Math.Floor(beat / _beatsPerBar);

        bool isBeat = beatIndex != _lastBeatIndex;
        bool isDownbeat = barIndex != _lastBarIndex;
        _lastBeatIndex = beatIndex;
        _lastBarIndex = barIndex;

        return new BeatClockState(
            Bpm: _bpm,
            Confidence: _confidence,
            BeatPhase: beat - Math.Floor(beat),
            BarPhase: _timeline.PhaseAtTime(nowTicks, _beatsPerBar),
            BeatCount: beatIndex,
            BarNumber: barIndex,
            IsBeat: isBeat,
            IsDownbeat: isDownbeat,
            IsLocked: false,
            Source: _source,
            Candidates: _candidates);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _frames.FrameAvailable -= OnFrame;
    }
}
