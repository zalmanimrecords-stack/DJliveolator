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
        var magnitude = new double[(frame.Length / 2) + 1];
        MagnitudeSpectrum(frame, new double[frame.Length], new double[frame.Length], magnitude);
        return magnitude;
    }

    /// <summary>
    /// Magnitude spectrum into caller-owned buffers, allocating nothing.
    /// </summary>
    /// <remarks>
    /// The realtime path runs this ~86 times a second on the audio thread, where the allocating
    /// overload's three arrays (~40 KB a frame at 2048) are pure GC pressure — and a collection there
    /// is a dropout. A caller that reuses its scratch across frames pays nothing.
    /// <paramref name="re"/> and <paramref name="im"/> must be <paramref name="frame"/>'s length and are
    /// overwritten; <paramref name="magnitude"/> must be length n/2 + 1.
    /// </remarks>
    public static void MagnitudeSpectrum(
        ReadOnlySpan<double> frame, double[] re, double[] im, Span<double> magnitude)
    {
        ArgumentNullException.ThrowIfNull(re);
        ArgumentNullException.ThrowIfNull(im);

        int n = frame.Length;
        int bins = (n / 2) + 1;
        if (re.Length != n || im.Length != n)
            throw new ArgumentException($"re and im must both be exactly {n} long.", nameof(re));
        if (magnitude.Length != bins)
            throw new ArgumentException($"magnitude must be exactly {bins} long.", nameof(magnitude));

        frame.CopyTo(re);
        Array.Clear(im, 0, n); // Forward reads the imaginary part, and a reused buffer still holds the last frame's
        Forward(re, im);

        for (int i = 0; i < bins; i++)
            magnitude[i] = Math.Sqrt((re[i] * re[i]) + (im[i] * im[i]));
    }
}
