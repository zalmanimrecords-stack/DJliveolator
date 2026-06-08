namespace Liveolator.Core.Dsp;

/// <summary>
/// A stereo-linked, <b>true-peak look-ahead</b> brick-wall limiter for the post-crossfader master bus
/// (doc 11). Two decks can sum past full scale; this holds the master's <em>inter-sample</em> peak at or
/// below a configured true-peak ceiling (default −1.0 dBTP) so it never clips — not even after the DAC
/// reconstructs the waveform between samples.
/// </summary>
/// <remarks>
/// Pure, deterministic, and <b>allocation-free</b> in <see cref="Process"/> (every buffer — delay line,
/// detector history, gain-window deque, FIR coefficients — is sized once in the constructor), so it runs
/// safely on the realtime audio thread (doc 01: "no allocation on the audio thread") and unit-tests with
/// no native BASS.
///
/// <para><b>Design (highest-quality master limiter):</b></para>
/// <list type="number">
/// <item><b>True-peak detector</b> — a 4× polyphase-FIR oversampled estimate of the inter-sample peak
/// per channel, stereo-linked (the loudest channel drives one shared gain, preserving the image). A
/// sample-peak limiter misses inter-sample peaks that clip the DAC; this catches them.</item>
/// <item><b>Look-ahead</b> — the audio is delayed by a short window (default 5 ms) while the gain is
/// computed from the un-delayed detector, so the gain is already fully reduced <em>before</em> a peak
/// reaches the output. This removes the transient distortion a zero-look-ahead limiter gets from
/// hard-clamping during its attack.</item>
/// <item><b>Max-hold gain window</b> — a sliding minimum (monotonic deque) of the required gain over the
/// look-ahead horizon guarantees the upcoming peak is covered; a fast attack smooths the step so it is
/// click-free, and the look-ahead (≥ attack) lets the ramp complete in time.</item>
/// <item><b>Smoothed release</b> — a two-stage (cascaded one-pole) recovery rounds the gain curve to
/// avoid pumping and low-frequency distortion.</item>
/// </list>
/// A final per-sample hard guard remains a true brick-wall <em>safety net</em>; with look-ahead it does
/// not engage on normal program material. Adds <see cref="LatencySamples"/> of latency (the look-ahead);
/// because the beat-analysis tap reads the post-limiter signal, the audible output and the tap are
/// delayed equally, so the shared audio↔visual clock stays aligned (doc 00/03/11).
/// </remarks>
public sealed class MasterLimiter
{
    private const double DefaultCeilingDbTp = -1.0;  // true-peak ceiling (broadcast/streaming convention)
    private const double DefaultAttackMs = 1.0;      // effective now that look-ahead covers the ramp
    private const double DefaultReleaseMs = 150.0;    // musical recovery for 4-on-the-floor; avoids pumping
    private const double DefaultReleaseSmoothMs = 60.0; // 2nd-stage release smoother
    private const double DefaultLookaheadMs = 5.0;    // ≥ attack so the gain ramp completes before the peak
    private const int OversampleTaps = 16;            // FIR taps per oversampling phase (4× true-peak detector)
    private const int OversampleFactor = 4;

    private readonly int _channels;
    private readonly double _ceiling;            // linear, absolute (e.g. ~0.891 for −1.0 dBFS/dBTP)
    private readonly double _attackCoeff;        // one-pole factor when reducing gain (stage 1)
    private readonly double _releaseCoeff;       // one-pole factor when recovering gain (stage 1)
    private readonly double _releaseSmoothCoeff; // one-pole factor for the stage-2 release smoother
    private readonly int _lookahead;             // L: look-ahead in frames (== added latency)

    // Audio delay line: ring of interleaved frames, length L*channels. Output(frame f) = input(frame f-L).
    private readonly float[] _delay;
    private int _delayPos;

    // True-peak detector: per-channel ring of the last OversampleTaps input samples + the polyphase FIR
    // coefficients for phases 1..3 (phase 0 is the identity sample). All pre-sized; no runtime alloc.
    private readonly double[] _firRing;          // length channels * OversampleTaps
    private readonly double[] _fir;              // length (OversampleFactor-1) * OversampleTaps
    private int _firPos;

    // Sliding-minimum (monotonic) deque of per-frame target gains over the look-ahead window [f-L, f].
    private readonly long[] _dqFrame;
    private readonly double[] _dqValue;
    private readonly int _dqCapacity;
    private int _dqHead;                          // index of the front (current window minimum)
    private int _dqCount;
    private long _frameCounter;

    // Envelope state carried across buffers. _gain is the applied (stage-2) multiplier.
    private double _gain1 = 1.0;
    private double _gain = 1.0;

    /// <param name="sampleRate">Output sample rate in Hz; positive.</param>
    /// <param name="channels">Interleaved channel count (2 = stereo); positive.</param>
    /// <param name="ceilingDbTp">True-peak output ceiling in dB; must be ≤ 0 (full scale).</param>
    /// <param name="attackMs">Gain-reduction smoothing time in milliseconds; positive.</param>
    /// <param name="releaseMs">Gain-recovery smoothing time in milliseconds; positive.</param>
    /// <param name="releaseSmoothMs">Second-stage release smoothing time in milliseconds; positive.</param>
    /// <param name="lookaheadMs">Look-ahead window in milliseconds; positive (also the added latency).</param>
    public MasterLimiter(
        int sampleRate,
        int channels,
        double ceilingDbTp = DefaultCeilingDbTp,
        double attackMs = DefaultAttackMs,
        double releaseMs = DefaultReleaseMs,
        double releaseSmoothMs = DefaultReleaseSmoothMs,
        double lookaheadMs = DefaultLookaheadMs)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Channels must be positive.");
        if (ceilingDbTp > 0.0)
            throw new ArgumentOutOfRangeException(
                nameof(ceilingDbTp), ceilingDbTp, "Ceiling must be at or below 0 dB.");
        if (attackMs <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(attackMs), attackMs, "Attack must be positive.");
        if (releaseMs <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(releaseMs), releaseMs, "Release must be positive.");
        if (releaseSmoothMs <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(releaseSmoothMs), releaseSmoothMs, "Release smoothing must be positive.");
        if (lookaheadMs <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(lookaheadMs), lookaheadMs, "Look-ahead must be positive.");

        _channels = channels;
        _ceiling = Math.Pow(10.0, ceilingDbTp / 20.0);
        _attackCoeff = OnePoleCoefficient(attackMs, sampleRate);
        _releaseCoeff = OnePoleCoefficient(releaseMs, sampleRate);
        _releaseSmoothCoeff = OnePoleCoefficient(releaseSmoothMs, sampleRate);
        _lookahead = Math.Max(1, (int)Math.Round(lookaheadMs * 0.001 * sampleRate));

        _delay = new float[_lookahead * channels];
        _firRing = new double[channels * OversampleTaps];
        _fir = BuildPolyphaseFir();

        // The window [f-L, f] spans L+1 frames; the deque holds at most that many distinct minima.
        _dqCapacity = _lookahead + 1;
        _dqFrame = new long[_dqCapacity];
        _dqValue = new double[_dqCapacity];
    }

    /// <summary>The current applied gain multiplier (1.0 = no reduction). Exposed for tests/metering.</summary>
    public double CurrentGain => _gain;

    /// <summary>The latency in samples (per channel) the look-ahead adds to the master signal.</summary>
    public int LatencySamples => _lookahead;

    /// <summary>
    /// Clears all internal state (delay line, detector history, gain window, envelope) so a re-routed or
    /// re-initialised output starts clean with no stale look-ahead tail. Not realtime-safe to call
    /// concurrently with <see cref="Process"/>; call it while the audio path is stopped.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_delay);
        Array.Clear(_firRing);
        _delayPos = 0;
        _firPos = 0;
        _dqHead = 0;
        _dqCount = 0;
        _frameCounter = 0;
        _gain1 = 1.0;
        _gain = 1.0;
    }

    /// <summary>
    /// Limit an interleaved float buffer in place. Allocation-free; safe on the audio thread. The buffer
    /// length must be a whole number of frames (a multiple of the channel count). Output is the input
    /// delayed by <see cref="LatencySamples"/> and gain-reduced so its true peak never exceeds the ceiling.
    /// </summary>
    public void Process(Span<float> interleaved)
    {
        if (interleaved.Length == 0)
            return;
        if (interleaved.Length % _channels != 0)
            throw new ArgumentException(
                $"Buffer length {interleaved.Length} is not a multiple of channel count {_channels}.",
                nameof(interleaved));

        int frames = interleaved.Length / _channels;
        for (int frame = 0; frame < frames; frame++)
        {
            int baseIdx = frame * _channels;

            // (1) Stereo-linked TRUE-PEAK detect on the incoming (un-delayed) frame; sanitise non-finite
            //     input so a corrupted upstream sample can never poison the detector or the envelope.
            double truePeak = 0.0;
            for (int c = 0; c < _channels; c++)
            {
                double s = interleaved[baseIdx + c];
                if (!double.IsFinite(s))
                    s = 0.0;
                double tpC = TruePeakChannel(c, s);
                if (tpC > truePeak)
                    truePeak = tpC;
            }

            // (2) Instantaneous target gain pulls the true peak to the ceiling; never above unity.
            double target = truePeak > _ceiling ? _ceiling / truePeak : 1.0;

            // (3) Sliding minimum of the target over the look-ahead window [f-L, f] (max-hold of reduction).
            double windowMin = PushTargetAndWindowMin(target);

            // (4) Two-stage smoothing. Stage 1: fast attack down / slow release up. Stage 2: follow the
            //     attack immediately (the look-ahead already gives the ramp room) but smooth the release.
            double coeff = windowMin < _gain1 ? _attackCoeff : _releaseCoeff;
            _gain1 += (windowMin - _gain1) * coeff;
            if (_gain1 < _gain)
                _gain = _gain1;
            else
                _gain += (_gain1 - _gain) * _releaseSmoothCoeff;

            // (5) Output the delayed frame at the applied gain, write the new frame into the delay line,
            //     and keep a hard safety net so the output is a true brick wall even on a degenerate edge.
            int slot = _delayPos * _channels;
            for (int c = 0; c < _channels; c++)
            {
                double delayed = _delay[slot + c];
                double incoming = interleaved[baseIdx + c];
                _delay[slot + c] = double.IsFinite(incoming) ? (float)incoming : 0f;

                double y = delayed * _gain;
                if (y > _ceiling)
                    y = _ceiling;
                else if (y < -_ceiling)
                    y = -_ceiling;
                else if (!double.IsFinite(y))
                    y = 0.0;
                interleaved[baseIdx + c] = (float)y;
            }

            // Advance the detector ring once per frame (all channels shared this frame's slot), then the
            // delay-line position and the absolute frame counter the gain window evicts against.
            _firPos++;
            if (_firPos >= OversampleTaps)
                _firPos = 0;
            _delayPos++;
            if (_delayPos >= _lookahead)
                _delayPos = 0;
            _frameCounter++;
        }
    }

    // Estimate the true (inter-sample) peak magnitude near the current input sample of channel c, using a
    // 4× polyphase FIR: phase 0 is the sample itself; phases 1..3 are fractional-delay interpolations.
    private double TruePeakChannel(int channel, double sample)
    {
        int ringBase = channel * OversampleTaps;
        _firRing[ringBase + _firPos] = sample;   // newest sample at _firPos

        double peak = Math.Abs(sample);          // phase 0 (identity)
        for (int phase = 0; phase < OversampleFactor - 1; phase++)
        {
            double acc = 0.0;
            int coeffBase = phase * OversampleTaps;
            for (int t = 0; t < OversampleTaps; t++)
            {
                // history[t] = sample (channel) t steps ago; _firPos holds the newest (t = 0).
                int idx = _firPos - t;
                if (idx < 0)
                    idx += OversampleTaps;
                acc += _fir[coeffBase + t] * _firRing[ringBase + idx];
            }
            double mag = Math.Abs(acc);
            if (mag > peak)
                peak = mag;
        }
        return peak;
    }

    // Push this frame's target gain and return the sliding minimum over the look-ahead window [f-L, f]
    // via a monotonic deque (values increase front→back, so the front is the running minimum).
    private double PushTargetAndWindowMin(double target)
    {
        long f = _frameCounter;

        // Maintain values increasing from front→back so the front is the window minimum.
        while (_dqCount > 0)
        {
            int backIdx = _dqHead + _dqCount - 1;
            if (backIdx >= _dqCapacity)
                backIdx -= _dqCapacity;
            if (_dqValue[backIdx] >= target)
                _dqCount--;
            else
                break;
        }

        int insert = _dqHead + _dqCount;
        if (insert >= _dqCapacity)
            insert -= _dqCapacity;
        _dqFrame[insert] = f;
        _dqValue[insert] = target;
        _dqCount++;

        // Evict the front while it is older than the window [f-L, f].
        while (_dqCount > 0 && _dqFrame[_dqHead] < f - _lookahead)
        {
            _dqHead++;
            if (_dqHead >= _dqCapacity)
                _dqHead = 0;
            _dqCount--;
        }

        return _dqValue[_dqHead];
    }

    private double[] BuildPolyphaseFir()
    {
        // Windowed-sinc fractional-delay interpolators for phases 1..3 (fractions 1/4, 2/4, 3/4). Each
        // estimates the signal between input samples; the detector takes the max magnitude across phases.
        var coeffs = new double[(OversampleFactor - 1) * OversampleTaps];
        double center = OversampleTaps / 2.0 - 1.0;   // keep the interpolation near the window centre
        for (int phase = 1; phase < OversampleFactor; phase++)
        {
            double frac = (double)phase / OversampleFactor;
            int baseIdx = (phase - 1) * OversampleTaps;
            for (int t = 0; t < OversampleTaps; t++)
            {
                double x = center - t - frac;
                coeffs[baseIdx + t] = Sinc(x) * HannWindow(t, OversampleTaps);
            }
        }
        return coeffs;
    }

    private static double Sinc(double x)
    {
        if (Math.Abs(x) < 1e-9)
            return 1.0;
        double px = Math.PI * x;
        return Math.Sin(px) / px;
    }

    private static double HannWindow(int t, int length)
        => 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * t / (length - 1));

    // One-pole smoothing coefficient for a given time constant: per-sample step toward the target.
    private static double OnePoleCoefficient(double timeMs, int sampleRate)
    {
        double samples = timeMs * 0.001 * sampleRate;
        if (samples <= 0.0)
            return 1.0;
        // 1 - e^(-1/τ): reaches ~63% of the step within the time constant.
        return 1.0 - Math.Exp(-1.0 / samples);
    }
}
