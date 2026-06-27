using Liveolator.Core.Mixer;

namespace Liveolator.Core.Waveform;

/// <summary>
/// A stateful biquad section over the shared RBJ designs in <see cref="MixerMath"/> (Butterworth Q),
/// used to split the waveform overview into low/mid/high bands. A time-domain filter — not an FFT —
/// on purpose: it runs sample-serially with no window, so a kick's attack lands in exactly the right
/// bucket instead of being smeared across an analysis window (the reason time-aligned waveforms are
/// banded with filters rather than an FFT). One instance per band per build, never shared. The realtime EQ keeps its
/// own per-channel state in the audio binding (<c>StatefulBiquad</c>) — this one is offline/analysis.
/// </summary>
public sealed class BiquadFilter
{
    private readonly float _b0, _b1, _b2, _a1, _a2;
    private float _z1, _z2;

    private BiquadFilter(BiquadCoefficients coefficients)
    {
        _b0 = (float)coefficients.B0;
        _b1 = (float)coefficients.B1;
        _b2 = (float)coefficients.B2;
        _a1 = (float)coefficients.A1;
        _a2 = (float)coefficients.A2;
    }

    /// <summary>2nd-order Butterworth low-pass at <paramref name="cutoffHz"/>.</summary>
    public static BiquadFilter LowPass(double cutoffHz, int sampleRate)
        => new(MixerMath.LowPass(cutoffHz, ValidateDesign(cutoffHz, sampleRate)));

    /// <summary>2nd-order Butterworth high-pass at <paramref name="cutoffHz"/>.</summary>
    public static BiquadFilter HighPass(double cutoffHz, int sampleRate)
        => new(MixerMath.HighPass(cutoffHz, ValidateDesign(cutoffHz, sampleRate)));

    // Strict, unlike the mixer's clamp-to-range design (a knob sweep must never throw mid-performance;
    // an analysis caller passing a crossover outside Nyquist is a programming error worth surfacing).
    private static int ValidateDesign(double cutoffHz, int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        if (cutoffHz <= 0 || cutoffHz >= sampleRate / 2.0)
            throw new ArgumentOutOfRangeException(
                nameof(cutoffHz), cutoffHz, "Cutoff must lie strictly between 0 and Nyquist (sampleRate/2).");
        return sampleRate;
    }

    /// <summary>Filter one sample, advancing the section's state (Direct Form II transposed).</summary>
    public float Process(float x)
    {
        float y = (_b0 * x) + _z1;
        _z1 = (_b1 * x) - (_a1 * y) + _z2;
        _z2 = (_b2 * x) - (_a2 * y);
        return y;
    }
}
