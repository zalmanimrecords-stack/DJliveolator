using Liveolator.Core.Audio;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

/// <summary>
/// Round-trip math between the deck's 0..1 hot-cue fraction and the persisted sample offset (A3).
/// </summary>
public class HotCuePositionMapperTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public void FractionToSamples_MidTrack_IsExact()
    {
        // 0.5 of a 100 s track at 48 kHz = 50 s * 48000 = 2_400_000 samples.
        Assert.Equal(2_400_000L, HotCuePositionMapper.FractionToSamples(0.5, 100.0, SampleRate));
    }

    [Fact]
    public void SamplesToFraction_IsInverseOfFractionToSamples()
    {
        long samples = HotCuePositionMapper.FractionToSamples(0.42, 100.0, SampleRate);
        double back = HotCuePositionMapper.SamplesToFraction(samples, 100.0, SampleRate);
        Assert.Equal(0.42, back, precision: 6);
    }

    [Fact]
    public void FractionToSamples_ClampsAboveOne()
    {
        Assert.Equal(
            HotCuePositionMapper.FractionToSamples(1.0, 100.0, SampleRate),
            HotCuePositionMapper.FractionToSamples(1.5, 100.0, SampleRate));
    }

    [Fact]
    public void SamplesToFraction_PastEnd_ClampsToOne()
    {
        Assert.Equal(1.0, HotCuePositionMapper.SamplesToFraction(999_999_999L, 100.0, SampleRate), precision: 6);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void NonPositiveLength_DegradesToZero(double length)
    {
        Assert.Equal(0L, HotCuePositionMapper.FractionToSamples(0.5, length, SampleRate));
        Assert.Equal(0.0, HotCuePositionMapper.SamplesToFraction(1000, length, SampleRate), precision: 6);
    }

    [Fact]
    public void NonPositiveSampleRate_DegradesToZero()
    {
        Assert.Equal(0L, HotCuePositionMapper.FractionToSamples(0.5, 100.0, 0));
        Assert.Equal(0.0, HotCuePositionMapper.SamplesToFraction(1000, 100.0, 0), precision: 6);
    }

    [Fact]
    public void SamplesToFraction_NegativeOrZeroSamples_IsZero()
    {
        Assert.Equal(0.0, HotCuePositionMapper.SamplesToFraction(0, 100.0, SampleRate), precision: 6);
        Assert.Equal(0.0, HotCuePositionMapper.SamplesToFraction(-10, 100.0, SampleRate), precision: 6);
    }
}
