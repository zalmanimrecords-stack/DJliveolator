using System.Collections.Generic;
using Liveolator.Core.Mixer;

namespace Liveolator.Core.Tests.Mixer;

/// <summary>Test double for <see cref="IMixer"/>: records the last value pushed for each deck slot.</summary>
internal sealed class FakeMixer : IMixer
{
    public Dictionary<int, double> DeckGain { get; } = new();
    public Dictionary<(int Slot, EqBand Band), BiquadCoefficients> Eq { get; } = new();
    public Dictionary<int, BiquadCoefficients> Filter { get; } = new();
    public Dictionary<int, bool> Cue { get; } = new();

    public void SetDeckGain(int slot, double linearGain) => DeckGain[slot] = linearGain;

    public void SetEqBand(int slot, EqBand band, BiquadCoefficients coefficients)
        => Eq[(slot, band)] = coefficients;

    public void SetFilter(int slot, BiquadCoefficients coefficients) => Filter[slot] = coefficients;

    public void SetCue(int slot, bool enabled) => Cue[slot] = enabled;
}
