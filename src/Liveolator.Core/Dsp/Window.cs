namespace Liveolator.Core.Dsp;

/// <summary>Analysis windows applied to frames before the FFT to reduce spectral leakage.</summary>
public static class Window
{
    /// <summary>
    /// Periodic Hann window of the given size (size &gt;= 2) — the correct form for STFT analysis, where
    /// frames are meant to tile seamlessly (constant-overlap-add). Uses denominator <c>size</c> (periodic),
    /// not <c>size - 1</c> (symmetric, for FIR design), so the window does not return to ~0 at the last
    /// sample and the per-frame bin energy is consistent (doc 27 low-severity fix).
    /// </summary>
    public static double[] Hann(int size)
    {
        if (size < 2)
            throw new ArgumentOutOfRangeException(nameof(size), "Window size must be >= 2.");

        var w = new double[size];
        for (int i = 0; i < size; i++)
            w[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / size));
        return w;
    }
}
