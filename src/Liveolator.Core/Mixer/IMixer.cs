namespace Liveolator.Core.Mixer;

/// <summary>
/// The realtime mixer seam (doc 11): Core computes the audible per-deck gain and the EQ/filter
/// biquad coefficients (see <see cref="MixerMath"/>) and pushes them here; the native binding
/// (Liveolator.Audio, a thin <c>BassMixer</c>) applies them to the BASS channels and BASS_FX.
/// Core depends only on this interface, so the mixer logic unit-tests with a fake — no native FX.
/// </summary>
/// <remarks>
/// Methods are deck-slot addressable (A = 0, B = 1) so the same seam scales to more decks later.
/// The handler calls these whenever a mixer action changes the relevant state; the binding holds
/// the most recent values and applies them per audio buffer.
/// </remarks>
public interface IMixer
{
    /// <summary>Set the combined linear output gain (channel gain × crossfader) for a deck slot.</summary>
    void SetDeckGain(int slot, double linearGain);

    /// <summary>Set the biquad coefficients for one EQ band of a deck slot.</summary>
    void SetEqBand(int slot, EqBand band, BiquadCoefficients coefficients);

    /// <summary>Set the single-knob filter biquad coefficients for a deck slot.</summary>
    void SetFilter(int slot, BiquadCoefficients coefficients);

    /// <summary>Route (or unroute) a deck slot to the headphone cue (PFL) bus.</summary>
    void SetCue(int slot, bool enabled);

    /// <summary>
    /// Set the headphone-cue output gains: how much of the summed cued (PFL) decks and of the master
    /// mix to send to the cue output, already scaled by the headphone level
    /// (see <see cref="CueMixMath.HeadphoneOutputGains"/>).
    /// </summary>
    void SetCueOutputGains(double cueGain, double masterGain);

    /// <summary>
    /// Apply the master smart-limiter controls (SAFE↔SMART, character, true-peak ceiling) to the
    /// realtime master-bus limiter. Core owns the authoritative <see cref="Dsp.LimiterSettings"/>; the
    /// binding forwards them to the running <see cref="Dsp.MasterLimiter"/> without changing its latency.
    /// </summary>
    void SetLimiter(Dsp.LimiterSettings settings);
}
