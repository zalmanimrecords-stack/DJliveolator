namespace Liveolator.Core.Mixer;

/// <summary>
/// Normalized Direct-Form-I biquad filter coefficients (a0 divided out). These are the design
/// output of <see cref="MixerMath"/> for an EQ band or the channel filter; the realtime binding
/// (Liveolator.Audio) feeds them to its DSP — Core only computes them, it never runs the sample
/// loop, keeping the audio math testable without native code (doc 11 "mixer math is pure").
/// </summary>
/// <param name="B0">Feed-forward coefficient for x[n].</param>
/// <param name="B1">Feed-forward coefficient for x[n-1].</param>
/// <param name="B2">Feed-forward coefficient for x[n-2].</param>
/// <param name="A1">Feedback coefficient for y[n-1].</param>
/// <param name="A2">Feedback coefficient for y[n-2].</param>
public readonly record struct BiquadCoefficients(double B0, double B1, double B2, double A1, double A2)
{
    /// <summary>The identity filter (passes the signal through unchanged).</summary>
    public static BiquadCoefficients Bypass { get; } = new(B0: 1, B1: 0, B2: 0, A1: 0, A2: 0);

    /// <summary>
    /// Applies this biquad to a single sample given the two previous input/output samples
    /// (Direct Form I). Provided so the pure math is fully unit-testable with a known signal,
    /// independent of any native DSP. Not the realtime path — that lives in the audio binding.
    /// </summary>
    public double Process(double x, double x1, double x2, double y1, double y2)
        => (B0 * x) + (B1 * x1) + (B2 * x2) - (A1 * y1) - (A2 * y2);
}
