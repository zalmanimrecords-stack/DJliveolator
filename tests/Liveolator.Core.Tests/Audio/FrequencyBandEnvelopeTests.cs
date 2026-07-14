using Liveolator.Core.Audio;

namespace Liveolator.Core.Tests.Audio;

public sealed class FrequencyBandEnvelopeTests
{
    [Theory]
    [InlineData(80, 0)]
    [InlineData(300, 1)]
    [InlineData(1_000, 2)]
    [InlineData(8_000, 3)]
    public void Process_MapsFrequencyIntoExpectedBand(double frequency, int expectedBand)
    {
        const int sampleRate = 48_000;
        const int fftSize = 2048;
        var spectrum = new float[fftSize / 2 + 1];
        spectrum[(int)Math.Round(frequency * fftSize / sampleRate)] = fftSize / 2f;
        var envelope = new FrequencyBandEnvelope(attackSeconds: 0.001, releaseSeconds: 0.2);

        envelope.Process(spectrum, sampleRate, 0);
        VisualAudioBands result = envelope.Process(spectrum, sampleRate, 0.1);
        double[] values = { result.Bass, result.LowMid, result.Mid, result.High };

        Assert.True(values[expectedBand] > 0.25);
        Assert.All(values.Where((_, index) => index != expectedBand), value => Assert.True(value < 0.01));
    }

    [Fact]
    public void Process_UsesFastAttackAndSlowerRelease()
    {
        const int sampleRate = 48_000;
        const int fftSize = 2048;
        var loud = new float[fftSize / 2 + 1];
        loud[4] = fftSize / 2f;
        var silent = new float[loud.Length];
        var envelope = new FrequencyBandEnvelope(attackSeconds: 0.01, releaseSeconds: 0.5);

        envelope.Process(loud, sampleRate, 0);
        double attacked = envelope.Process(loud, sampleRate, 0.05).Bass;
        double released = envelope.Process(silent, sampleRate, 0.05).Bass;

        Assert.True(attacked > 0.5);
        Assert.True(released > attacked * 0.8);
    }

    [Fact]
    public void Process_InvalidSpectrumLeavesSilence()
    {
        var envelope = new FrequencyBandEnvelope();

        Assert.Equal(VisualAudioBands.Silent, envelope.Process(Array.Empty<float>(), 0, 0.1));
    }
}
