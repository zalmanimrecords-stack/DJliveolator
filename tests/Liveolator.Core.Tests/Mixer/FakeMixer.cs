using System.Collections.Generic;
using Liveolator.Core.Dsp;
using Liveolator.Core.Mixer;

namespace Liveolator.Core.Tests.Mixer;

/// <summary>Test double for <see cref="IMixer"/>: records the last value pushed for each deck slot.</summary>
internal sealed class FakeMixer : IMixer
{
    public Dictionary<int, double> DeckGain { get; } = new();
    public Dictionary<(int Slot, EqBand Band), BiquadCoefficients> Eq { get; } = new();
    public Dictionary<int, BiquadCoefficients> Filter { get; } = new();
    public Dictionary<int, bool> Cue { get; } = new();
    public (double CueGain, double MasterGain)? CueOutputGains { get; private set; }

    /// <summary>The most recent limiter settings pushed (null until <see cref="SetLimiter"/> is called).</summary>
    public LimiterSettings? Limiter { get; private set; }

    public void SetDeckGain(int slot, double linearGain) => DeckGain[slot] = linearGain;

    public void SetEqBand(int slot, EqBand band, BiquadCoefficients coefficients)
        => Eq[(slot, band)] = coefficients;

    public void SetFilter(int slot, BiquadCoefficients coefficients) => Filter[slot] = coefficients;

    public void SetCue(int slot, bool enabled) => Cue[slot] = enabled;

    public void SetCueOutputGains(double cueGain, double masterGain)
        => CueOutputGains = (cueGain, masterGain);

    public void SetLimiter(LimiterSettings settings) => Limiter = settings;
}
