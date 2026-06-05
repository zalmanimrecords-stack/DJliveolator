using Liveolator.Core.Mixer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Audio.Playback;

/// <summary>
/// The realtime side of the software mixer (doc 11): implements the Core <see cref="IMixer"/> seam
/// by forwarding each slot's gain/EQ/filter/cue to its BASS channel. Core computes all the DSP
/// numbers (<see cref="MixerMath"/>); this type only routes them to BASS_FX, keeping the binding
/// thin and the math testable without native code. Channels are registered per deck slot by the
/// two-deck BASS engine as decks are loaded.
/// </summary>
/// <remarks>
/// This increment delivers the routing skeleton. The concrete <see cref="IBassMixerChannel"/> that
/// issues BASS_FX biquad calls lands with the two-deck BASS engine (next increment); until a slot's
/// channel is registered, calls for that slot are logged and dropped rather than crashing the audio
/// path (global standard #26 — never fail silently).
/// </remarks>
public sealed class BassMixer : IMixer
{
    private readonly IBassMixerChannel?[] _channels;
    private readonly ILogger _logger;

    public BassMixer(int deckCount = MixerState.DeckCount, ILoggerFactory? loggerFactory = null)
    {
        if (deckCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(deckCount), deckCount, "Deck count must be positive.");
        _channels = new IBassMixerChannel?[deckCount];
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<BassMixer>();
    }

    /// <summary>Number of deck slots this mixer addresses.</summary>
    public int DeckCount => _channels.Length;

    /// <summary>
    /// Registers (or clears) the BASS channel for a deck slot. Called by the two-deck engine when a
    /// deck is loaded/unloaded so the mixer can route FX to it. Internal: callers live in this binding.
    /// </summary>
    internal void SetChannel(int slot, IBassMixerChannel? channel)
    {
        EnsureSlot(slot);
        _channels[slot] = channel;
    }

    public void SetDeckGain(int slot, double linearGain)
    {
        if (TryChannel(slot, nameof(SetDeckGain), out IBassMixerChannel channel))
            channel.SetVolume(linearGain);
    }

    public void SetEqBand(int slot, EqBand band, BiquadCoefficients coefficients)
    {
        if (TryChannel(slot, nameof(SetEqBand), out IBassMixerChannel channel))
            channel.SetEqBand(band, coefficients);
    }

    public void SetFilter(int slot, BiquadCoefficients coefficients)
    {
        if (TryChannel(slot, nameof(SetFilter), out IBassMixerChannel channel))
            channel.SetFilter(coefficients);
    }

    public void SetCue(int slot, bool enabled)
    {
        if (TryChannel(slot, nameof(SetCue), out IBassMixerChannel channel))
            channel.SetCue(enabled);
    }

    private bool TryChannel(int slot, string op, out IBassMixerChannel channel)
    {
        EnsureSlot(slot);
        IBassMixerChannel? c = _channels[slot];
        if (c is null)
        {
            // A mixer control moved before its deck was loaded — keep the latest UI state but
            // surface that it had no channel to apply to, rather than dropping it silently.
            _logger.LogDebug("{Op} for deck slot {Slot} ignored: no channel registered yet.", op, slot);
            channel = default!;
            return false;
        }
        channel = c;
        return true;
    }

    private void EnsureSlot(int slot)
    {
        if (slot < 0 || slot >= _channels.Length)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Deck slot is out of range.");
    }
}
