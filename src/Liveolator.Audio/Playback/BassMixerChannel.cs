using Liveolator.Core.Mixer;
using Liveolator.Core.Audio.Effects;

namespace Liveolator.Audio.Playback;

/// <summary>
/// The realtime per-deck channel processor (doc 11): applies the Core mixer's output gain and the
/// cascaded 3-band EQ + single-knob filter biquads (designed by <see cref="MixerMath"/>) to the deck's
/// samples. Implements <see cref="IBassMixerChannel"/> so <see cref="BassMixer"/> routes gain/EQ/filter
/// here; the actual sample work runs in <see cref="Process"/>, called from the deck's BASS DSP callback.
/// The processing is pure (given coefficients + gain) and unit-tested without native BASS.
/// </summary>
/// <remarks>
/// EQ bands cascade Low → Mid → High, then the filter, matching the signal flow the Core math assumes.
/// <see cref="SetCue"/> only latches the PFL flag for now; the dedicated headphone-cue output bus is a
/// later increment (doc 11 deferred), so cue is not yet a second audio path.
/// </remarks>
internal sealed class BassMixerChannel : IBassMixerChannel
{
    private readonly int _channels;
    private readonly StatefulBiquad _low;
    private readonly StatefulBiquad _mid;
    private readonly StatefulBiquad _high;
    private readonly StatefulBiquad _filter;
    private readonly IAudioEffectRack? _effects;
    private volatile float _gain = 1.0f;

    public BassMixerChannel(int channels, IAudioEffectRack? effects = null)
    {
        if (channels < 1)
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Channels must be positive.");
        _channels = channels;
        _low = new StatefulBiquad(channels);
        _mid = new StatefulBiquad(channels);
        _high = new StatefulBiquad(channels);
        _filter = new StatefulBiquad(channels);
        _effects = effects;
    }

    /// <summary>True while this deck is routed to the headphone cue (PFL) bus.</summary>
    public bool CueEnabled { get; private set; }

    public void SetVolume(double linearGain) => _gain = (float)linearGain;

    public void SetEqBand(EqBand band, BiquadCoefficients coefficients) => Band(band).SetCoefficients(coefficients);

    public void SetFilter(BiquadCoefficients coefficients) => _filter.SetCoefficients(coefficients);

    public void SetCue(bool enabled) => CueEnabled = enabled;

    /// <summary>
    /// Apply gain then the cascaded EQ + filter to an interleaved buffer in place. Runs on the BASS
    /// update thread; the per-channel biquad state makes stereo channels independent.
    /// </summary>
    public void Process(Span<float> interleaved, int channels)
    {
        if (channels != _channels)
            throw new ArgumentException(
                $"Channel processor built for {_channels} channel(s), got {channels}.", nameof(channels));

        float gain = _gain;
        int frames = interleaved.Length / channels;
        for (int f = 0; f < frames; f++)
        {
            int baseIdx = f * channels;
            for (int c = 0; c < channels; c++)
            {
                double s = interleaved[baseIdx + c] * gain;
                s = _low.Process(c, s);
                s = _mid.Process(c, s);
                s = _high.Process(c, s);
                s = _filter.Process(c, s);
                interleaved[baseIdx + c] = (float)s;
            }
        }
        _effects?.Process(interleaved, channels);
    }

    private StatefulBiquad Band(EqBand band) => band switch
    {
        EqBand.Low => _low,
        EqBand.Mid => _mid,
        EqBand.High => _high,
        _ => throw new ArgumentOutOfRangeException(nameof(band), band, "Unknown EQ band."),
    };
}
