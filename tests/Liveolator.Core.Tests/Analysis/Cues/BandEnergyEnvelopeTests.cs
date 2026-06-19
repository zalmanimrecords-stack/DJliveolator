using Liveolator.Core.Analysis.Cues;
using Xunit;

namespace Liveolator.Core.Tests.Analysis.Cues;

public class BandEnergyEnvelopeTests
{
    private const int Sr = 44100;

    [Fact]
    public void Compute_SignalShorterThanFrame_ReturnsEmpty()
    {
        BandEnergyFrames frames = new BandEnergyEnvelope(frameSize: 1024).Compute(new float[512], Sr);

        Assert.Equal(0, frames.FrameCount);
        Assert.Equal(0.0, frames.FrameRateHz);
    }

    [Fact]
    public void Compute_LowTone_ConcentratesEnergyInLowBand()
    {
        var tone = TestSignals.Sine(60, Sr, seconds: 1.0);

        BandEnergyFrames frames = new BandEnergyEnvelope().Compute(tone, Sr);

        Assert.True(frames.FrameCount > 0);
        Assert.True(BandSum(frames.Low) > BandSum(frames.Mid));
        Assert.True(BandSum(frames.Low) > BandSum(frames.High));
    }

    [Fact]
    public void Compute_MidTone_ConcentratesEnergyInMidBand()
    {
        var tone = TestSignals.Sine(1000, Sr, seconds: 1.0);

        BandEnergyFrames frames = new BandEnergyEnvelope().Compute(tone, Sr);

        Assert.True(BandSum(frames.Mid) > BandSum(frames.Low));
        Assert.True(BandSum(frames.Mid) > BandSum(frames.High));
    }

    [Fact]
    public void Compute_HighTone_ConcentratesEnergyInHighBand()
    {
        var tone = TestSignals.Sine(6000, Sr, seconds: 1.0);

        BandEnergyFrames frames = new BandEnergyEnvelope().Compute(tone, Sr);

        Assert.True(BandSum(frames.High) > BandSum(frames.Low));
        Assert.True(BandSum(frames.High) > BandSum(frames.Mid));
    }

    [Fact]
    public void Compute_Broadband_EqualsSumOfBands()
    {
        var signal = TestSignals.Chord(new[] { (60.0, 0.5), (1000.0, 0.5), (6000.0, 0.5) }, Sr, seconds: 0.5);

        BandEnergyFrames frames = new BandEnergyEnvelope().Compute(signal, Sr);

        for (int f = 0; f < frames.FrameCount; f++)
        {
            double sum = frames.Low[f] + frames.Mid[f] + frames.High[f];
            Assert.Equal(frames.Broadband[f], sum, precision: 6);
        }
    }

    [Fact]
    public void FrameSeconds_MapsIndexToTimeViaFrameRate()
    {
        BandEnergyFrames frames = new BandEnergyEnvelope(hop: 512).Compute(TestSignals.Sine(440, Sr, 1.0), Sr);

        double expectedRate = (double)Sr / 512;
        Assert.Equal(expectedRate, frames.FrameRateHz, precision: 6);
        Assert.Equal(100 / expectedRate, frames.FrameSeconds(100), precision: 6);
    }

    private static double BandSum(double[] band)
    {
        double total = 0.0;
        foreach (double v in band)
            total += v;
        return total;
    }
}
