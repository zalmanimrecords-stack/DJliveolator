using Liveolator.Core.Dsp;
using Xunit;

namespace Liveolator.Core.Tests.Dsp;

public class FftTests
{
    [Fact]
    public void Impulse_ProducesFlatMagnitudeSpectrum()
    {
        var frame = new double[8];
        frame[0] = 1.0; // unit impulse → all bins have magnitude 1

        double[] mag = Fft.MagnitudeSpectrum(frame);

        Assert.Equal(8 / 2 + 1, mag.Length);
        Assert.All(mag, m => Assert.Equal(1.0, m, precision: 9));
    }

    [Fact]
    public void SingleCosine_PeaksAtItsBin()
    {
        const int n = 16;
        const int k = 2;
        var frame = new double[n];
        for (int i = 0; i < n; i++)
            frame[i] = Math.Cos(2.0 * Math.PI * k * i / n);

        double[] mag = Fft.MagnitudeSpectrum(frame);

        int peakBin = 0;
        for (int i = 1; i < mag.Length; i++)
            if (mag[i] > mag[peakBin]) peakBin = i;

        Assert.Equal(k, peakBin);
        Assert.Equal(n / 2.0, mag[k], precision: 6); // real cosine → n/2 at its bin
    }

    [Fact]
    public void Forward_NonPowerOfTwo_Throws()
    {
        var re = new double[3];
        var im = new double[3];
        Assert.Throws<ArgumentException>(() => Fft.Forward(re, im));
    }
}
