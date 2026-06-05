using Liveolator.Core.Mixer;

namespace Liveolator.Audio.Playback;

/// <summary>
/// A realtime Direct-Form-I biquad that runs the sample loop the Core <see cref="BiquadCoefficients"/>
/// only describes (doc 11: "Core computes the numbers, the binding applies them"). Holds per-audio-channel
/// delay state so a stereo deck filters each channel independently, and lets the coefficients be swapped
/// live (EQ/filter knob moves) without resetting history — so a turn of the knob doesn't click.
/// </summary>
internal sealed class StatefulBiquad
{
    private readonly double[] _x1;
    private readonly double[] _x2;
    private readonly double[] _y1;
    private readonly double[] _y2;
    private BiquadCoefficients _coefficients = BiquadCoefficients.Bypass;

    public StatefulBiquad(int channels)
    {
        if (channels < 1)
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Channels must be positive.");
        _x1 = new double[channels];
        _x2 = new double[channels];
        _y1 = new double[channels];
        _y2 = new double[channels];
    }

    /// <summary>Swap the active coefficients; delay history is preserved so the change is click-free.</summary>
    public void SetCoefficients(BiquadCoefficients coefficients) => _coefficients = coefficients;

    /// <summary>Filter one sample for the given audio channel, advancing that channel's delay line.</summary>
    public double Process(int channel, double x)
    {
        double y = _coefficients.Process(x, _x1[channel], _x2[channel], _y1[channel], _y2[channel]);
        _x2[channel] = _x1[channel];
        _x1[channel] = x;
        _y2[channel] = _y1[channel];
        _y1[channel] = y;
        return y;
    }
}
