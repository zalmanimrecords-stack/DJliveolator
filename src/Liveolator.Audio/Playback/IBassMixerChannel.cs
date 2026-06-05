using Liveolator.Core.Mixer;

namespace Liveolator.Audio.Playback;

/// <summary>
/// Thin seam over the per-deck BASS calls the software mixer needs (channel volume, BASS_FX biquad
/// for EQ/filter, cue routing), so <see cref="BassMixer"/> can be unit-tested with a fake while the
/// real BASS_FX P/Invoke is isolated. Internal: a binding implementation detail, mirroring the
/// <see cref="IBassPlayback"/> pattern.
/// </summary>
internal interface IBassMixerChannel
{
    /// <summary>Set the channel's linear output volume (0..1).</summary>
    void SetVolume(double linearGain);

    /// <summary>Apply a biquad to one EQ band of this channel (via BASS_FX).</summary>
    void SetEqBand(EqBand band, BiquadCoefficients coefficients);

    /// <summary>Apply the single-knob filter biquad to this channel (via BASS_FX).</summary>
    void SetFilter(BiquadCoefficients coefficients);

    /// <summary>Route (or unroute) this channel to the headphone cue (PFL) bus.</summary>
    void SetCue(bool enabled);
}
