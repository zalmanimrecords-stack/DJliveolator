namespace Liveolator.Core.Audio;

/// <summary>
/// Pure VU-meter ballistics over the shared analysis frames (doc 26). Each call computes the frame's
/// block RMS + peak and advances a smoothed "VU" value with a fast attack / slow release so a meter
/// needle punches up with the music and eases back — the same look an analog VU meter has.
/// </summary>
/// <remarks>
/// GL-free and hardware-free, so the ballistics unit-test off the GPU (the iron Core rule). The
/// smoothing is driven by the elapsed wall-clock <c>dt</c> between frames (taken from successive
/// <see cref="AudioFrameData.TimestampSeconds"/>), so the needle's physics are independent of the
/// display frame rate — the same reason <see cref="Beat.AudioBeatClock"/> derives its envelope rate
/// from frame timestamps. Stateful and single-writer: it is fed from one audio frame thread.
/// </remarks>
public sealed class AudioLevelEnvelope
{
    private readonly double _attackSeconds;
    private readonly double _releaseSeconds;
    private double _vu;

    /// <param name="attackSeconds">Time constant when the level is rising (fast). Must be &gt; 0.</param>
    /// <param name="releaseSeconds">Time constant when the level is falling (slow). Must be &gt; 0.</param>
    public AudioLevelEnvelope(double attackSeconds = 0.05, double releaseSeconds = 0.3)
    {
        if (attackSeconds <= 0 || double.IsNaN(attackSeconds))
            throw new ArgumentOutOfRangeException(nameof(attackSeconds), attackSeconds, "Attack must be > 0.");
        if (releaseSeconds <= 0 || double.IsNaN(releaseSeconds))
            throw new ArgumentOutOfRangeException(nameof(releaseSeconds), releaseSeconds, "Release must be > 0.");

        _attackSeconds = attackSeconds;
        _releaseSeconds = releaseSeconds;
    }

    /// <summary>
    /// Computes the next level snapshot from one mono analysis frame and the seconds elapsed since the
    /// previous frame. A non-positive or NaN <paramref name="dtSeconds"/> (e.g. the first frame) leaves
    /// the smoothed VU at rest and only reports the instantaneous RMS/peak, so the meter starts at the
    /// floor and rises with audio rather than snapping.
    /// </summary>
    public VisualAudioLevel Process(ReadOnlySpan<float> monoPcm, double dtSeconds)
    {
        double rms = 0.0;
        double peak = 0.0;
        if (monoPcm.Length > 0)
        {
            double sumSquares = 0.0;
            for (int i = 0; i < monoPcm.Length; i++)
            {
                double sample = monoPcm[i];
                if (double.IsNaN(sample))
                    continue;
                double magnitude = Math.Abs(sample);
                if (magnitude > peak)
                    peak = magnitude;
                sumSquares += sample * sample;
            }
            rms = Math.Sqrt(sumSquares / monoPcm.Length);
        }

        rms = Math.Clamp(rms, 0.0, 1.0);
        peak = Math.Clamp(peak, 0.0, 1.0);

        // Exponential smoothing toward the RMS target; the coefficient grows with dt so longer gaps
        // move further. Rising uses the short (attack) constant, falling the long (release) constant.
        if (dtSeconds > 0.0 && !double.IsNaN(dtSeconds))
        {
            double tau = rms > _vu ? _attackSeconds : _releaseSeconds;
            double coefficient = 1.0 - Math.Exp(-dtSeconds / tau);
            _vu += (rms - _vu) * coefficient;
            _vu = Math.Clamp(_vu, 0.0, 1.0);
        }

        return new VisualAudioLevel(rms, peak, _vu);
    }
}
