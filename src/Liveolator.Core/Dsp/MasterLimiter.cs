namespace Liveolator.Core.Dsp;

/// <summary>
/// A stereo-linked feed-forward brick-wall peak limiter for the post-crossfader master bus
/// (doc 11). Two decks can sum past full scale; this keeps the master peak at or below a
/// configured ceiling (default −0.1 dBFS) so it never hard-clips.
/// </summary>
/// <remarks>
/// Pure, deterministic, and allocation-free in <see cref="Process"/> — it owns no heap state
/// beyond the gain envelope, so it runs safely on the realtime audio thread (doc 01:
/// "no allocation on the audio thread") and unit-tests without native BASS.
///
/// Design: per audio frame it takes the max absolute sample across all channels (stereo-linked,
/// so the same gain is applied to every channel and the stereo image is preserved). The target
/// gain is the ratio that pulls that peak down to the ceiling (≤ 1.0, never makeup gain). The
/// applied gain follows the target through two one-pole smoothers — a fast attack when the gain
/// must drop and a slower release when it may recover — giving click-free, transient-safe
/// limiting. A final per-sample hard guard catches the sub-attack-time overshoot so the output
/// is a true brick wall (never above full scale) even on an instantaneous overload.
/// </remarks>
public sealed class MasterLimiter
{
    private const double DefaultCeilingDbfs = -0.1;
    private const double DefaultAttackMs = 1.0;   // fast enough to catch transients, slow enough to be click-free
    private const double DefaultReleaseMs = 100.0; // musical recovery; avoids audible pumping

    private readonly int _channels;
    private readonly double _ceiling;       // linear, absolute (e.g. ~0.988 for −0.1 dBFS)
    private readonly double _attackCoeff;   // one-pole smoothing factor when reducing gain
    private readonly double _releaseCoeff;  // one-pole smoothing factor when recovering gain

    // The only mutable state: the current gain multiplier carried across buffers so attack/release
    // are continuous. Starts at unity (no reduction).
    private double _gain = 1.0;

    /// <param name="sampleRate">Output sample rate in Hz; positive.</param>
    /// <param name="channels">Interleaved channel count (2 = stereo); positive.</param>
    /// <param name="ceilingDbfs">Output ceiling in dBFS; must be ≤ 0 (full scale).</param>
    /// <param name="attackMs">Gain-reduction smoothing time in milliseconds; positive.</param>
    /// <param name="releaseMs">Gain-recovery smoothing time in milliseconds; positive.</param>
    public MasterLimiter(
        int sampleRate,
        int channels,
        double ceilingDbfs = DefaultCeilingDbfs,
        double attackMs = DefaultAttackMs,
        double releaseMs = DefaultReleaseMs)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Channels must be positive.");
        if (ceilingDbfs > 0.0)
            throw new ArgumentOutOfRangeException(
                nameof(ceilingDbfs), ceilingDbfs, "Ceiling must be at or below 0 dBFS.");
        if (attackMs <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(attackMs), attackMs, "Attack must be positive.");
        if (releaseMs <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(releaseMs), releaseMs, "Release must be positive.");

        _channels = channels;
        _ceiling = Math.Pow(10.0, ceilingDbfs / 20.0);
        _attackCoeff = OnePoleCoefficient(attackMs, sampleRate);
        _releaseCoeff = OnePoleCoefficient(releaseMs, sampleRate);
    }

    /// <summary>The current gain multiplier (1.0 = no reduction). Exposed for tests/metering.</summary>
    public double CurrentGain => _gain;

    /// <summary>
    /// Limit an interleaved float buffer in place. Allocation-free; safe on the audio thread.
    /// The buffer length must be a whole number of frames (a multiple of the channel count).
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

            // Stereo-linked detector: the loudest channel in this frame drives the gain.
            double peak = 0.0;
            for (int c = 0; c < _channels; c++)
            {
                double abs = Math.Abs(interleaved[baseIdx + c]);
                if (abs > peak)
                    peak = abs;
            }

            // Target gain pulls the peak down to the ceiling; never above unity (no makeup gain).
            double targetGain = peak > _ceiling ? _ceiling / peak : 1.0;

            // One-pole toward the target: fast when clamping down (attack), slow when releasing up.
            double coeff = targetGain < _gain ? _attackCoeff : _releaseCoeff;
            _gain += (targetGain - _gain) * coeff;

            // Apply the shared gain to every channel so the stereo image is unchanged, then hard-guard
            // against the residual overshoot the finite attack time can let through, making it a true
            // brick wall without ever exceeding full scale.
            for (int c = 0; c < _channels; c++)
            {
                double limited = interleaved[baseIdx + c] * _gain;
                if (limited > _ceiling)
                    limited = _ceiling;
                else if (limited < -_ceiling)
                    limited = -_ceiling;
                interleaved[baseIdx + c] = (float)limited;
            }
        }
    }

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
