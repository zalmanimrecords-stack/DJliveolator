namespace Liveolator.Core.Dsp;

/// <summary>
/// Iterative in-place radix-2 Cooley–Tukey FFT over double precision buffers.
/// Pure, allocation-light, and platform-agnostic so it unit-tests without native deps.
/// </summary>
public static class Fft
{
    /// <summary>
    /// In-place forward FFT. <paramref name="real"/> and <paramref name="imag"/> must be the
    /// same length and that length must be a power of two.
    /// </summary>
    public static void Forward(double[] real, double[] imag)
    {
        ArgumentNullException.ThrowIfNull(real);
        ArgumentNullException.ThrowIfNull(imag);
        if (real.Length != imag.Length)
            throw new ArgumentException("real and imag buffers must have equal length.");

        int n = real.Length;
        if (n == 0) return;
        if ((n & (n - 1)) != 0)
            throw new ArgumentException($"FFT length must be a power of two, got {n}.");

        // Bit-reversal permutation.
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        // Butterfly stages.
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2.0 * Math.PI / len;
            double wLenRe = Math.Cos(ang);
            double wLenIm = Math.Sin(ang);
            int half = len >> 1;
            for (int i = 0; i < n; i += len)
            {
                double wRe = 1.0, wIm = 0.0;
                for (int k = 0; k < half; k++)
                {
                    int a = i + k;
                    int b = a + half;
                    double vRe = real[b] * wRe - imag[b] * wIm;
                    double vIm = real[b] * wIm + imag[b] * wRe;
                    real[b] = real[a] - vRe;
                    imag[b] = imag[a] - vIm;
                    real[a] += vRe;
                    imag[a] += vIm;
                    double nextWRe = wRe * wLenRe - wIm * wLenIm;
                    wIm = wRe * wLenIm + wIm * wLenRe;
                    wRe = nextWRe;
                }
            }
        }
    }

    /// <summary>
    /// Magnitude spectrum of a real-valued frame whose length is a power of two.
    /// Returns the non-redundant bins (length n/2 + 1).
    /// </summary>
    public static double[] MagnitudeSpectrum(ReadOnlySpan<double> frame)
    {
        int n = frame.Length;
        var re = new double[n];
        var im = new double[n];
        frame.CopyTo(re);
        Forward(re, im);

        int bins = n / 2 + 1;
        var mag = new double[bins];
        for (int i = 0; i < bins; i++)
            mag[i] = Math.Sqrt(re[i] * re[i] + im[i] * im[i]);
        return mag;
    }
}
