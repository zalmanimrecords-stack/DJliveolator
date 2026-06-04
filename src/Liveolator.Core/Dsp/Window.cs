namespace Liveolator.Core.Dsp;

/// <summary>Analysis windows applied to frames before the FFT to reduce spectral leakage.</summary>
public static class Window
{
    /// <summary>Periodic-symmetric Hann window of the given size (size &gt;= 2).</summary>
    public static double[] Hann(int size)
    {
        if (size < 2)
            throw new ArgumentOutOfRangeException(nameof(size), "Window size must be >= 2.");

        var w = new double[size];
        for (int i = 0; i < size; i++)
            w[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (size - 1)));
        return w;
    }
}
