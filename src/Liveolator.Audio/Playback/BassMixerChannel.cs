using Liveolator.Core.Mixer;
using Liveolator.Core.Audio.Effects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly ILogger _logger;
    private readonly int _slot;
    private volatile float _gain = 1.0f;
    private volatile float _peak;
    private volatile float _rms;
    // DIAG (jog-audible-at-zero-volume): throttle the muted-but-audible probe so a jog burst logs a few
    // lines, not one per audio buffer. Counts qualifying buffers; see Process. Remove once resolved.
    private int _leakLogCounter;

    public BassMixerChannel(int channels, IAudioEffectRack? effects = null, ILogger? logger = null, int slot = -1)
    {
        if (channels < 1)
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Channels must be positive.");
        _channels = channels;
        _low = new StatefulBiquad(channels);
        _mid = new StatefulBiquad(channels);
        _high = new StatefulBiquad(channels);
        _filter = new StatefulBiquad(channels);
        _effects = effects;
        _logger = logger ?? NullLogger.Instance;
        _slot = slot;
    }

    /// <summary>True while this deck is routed to the headphone cue (PFL) bus.</summary>
    public bool CueEnabled { get; private set; }

    public DeckLevel Level => new(_peak, _rms);

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

        double peak = 0;
        double sumSquares = 0;
        for (int i = 0; i < interleaved.Length; i++)
        {
            double magnitude = Math.Abs(interleaved[i]);
            peak = Math.Max(peak, magnitude);
            sumSquares += magnitude * magnitude;
        }
        _peak = (float)Math.Clamp(peak, 0.0, 1.0);
        _rms = interleaved.Length == 0
            ? 0
            : (float)Math.Clamp(Math.Sqrt(sumSquares / interleaved.Length), 0.0, 1.0);

        // DIAG (jog-audible-at-zero-volume): the channel fader is at/near zero (gain ~0) yet this channel
        // is still emitting audio past the gain stage — the exact reported bug. Logs the live gain + peak so
        // we can tell whether the gain is genuinely 0 (a native/state leak) or has been re-raised. Guarded so
        // it stays silent in normal use, and throttled to ~1 line per 16 qualifying buffers. Remove once fixed.
        if (gain < 1e-3f && _peak > 0.02f && (_leakLogCounter++ & 0xF) == 0)
            _logger.LogInformation(
                "DIAG mixer-leak slot {Slot}: gain={Gain:F5} but post-gain peak={Peak:F4} (muted channel still audible).",
                _slot, gain, _peak);
    }

    private StatefulBiquad Band(EqBand band) => band switch
    {
        EqBand.Low => _low,
        EqBand.Mid => _mid,
        EqBand.High => _high,
        _ => throw new ArgumentOutOfRangeException(nameof(band), band, "Unknown EQ band."),
    };
}
