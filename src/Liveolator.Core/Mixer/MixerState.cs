namespace Liveolator.Core.Mixer;

/// <summary>
/// The complete, immutable state of the two-deck software mixer (doc 11): the crossfader position,
/// the selected crossfader curve, and one <see cref="DeckChannelState"/> per deck slot. Pure data
/// with no behaviour — the math that derives audible gains lives in <see cref="MixerMath"/> and the
/// action wiring in <c>MixerActionHandler</c>. Two slots only for this increment (A = 0, B = 1);
/// kept as an indexable list so a later increment can grow without reshaping callers.
/// </summary>
/// <param name="Crossfader">Crossfader position 0..1: 0 = full deck A, 1 = full deck B, 0.5 = center.</param>
/// <param name="Curve">Shape of the crossfader transition.</param>
/// <param name="Channels">Per-deck channel strips, indexed by deck slot.</param>
/// <param name="CueBus">The headphone-cue (PFL) bus level and cue/master blend.</param>
public sealed record MixerState(
    double Crossfader,
    CrossfaderCurve Curve,
    IReadOnlyList<DeckChannelState> Channels,
    CueBusState CueBus)
{
    /// <summary>Number of deck slots in this increment.</summary>
    public const int DeckCount = 2;

    /// <summary>Deck slot index for deck A.</summary>
    public const int DeckA = 0;

    /// <summary>Deck slot index for deck B.</summary>
    public const int DeckB = 1;

    /// <summary>Crossfader centered, smooth curve, both decks at their default channel strip, cue bus default.</summary>
    public static MixerState Default { get; } = new(
        Crossfader: 0.5,
        Curve: CrossfaderCurve.Smooth,
        Channels: new[] { DeckChannelState.Default, DeckChannelState.Default },
        CueBus: CueBusState.Default);

    /// <summary>The channel strip for one deck slot.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Slot is outside 0..<see cref="DeckCount"/>-1.</exception>
    public DeckChannelState Channel(int slot)
    {
        if (slot < 0 || slot >= Channels.Count)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Deck slot is out of range.");
        return Channels[slot];
    }

    /// <summary>Returns a copy with one deck slot's channel strip replaced.</summary>
    public MixerState WithChannel(int slot, DeckChannelState channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (slot < 0 || slot >= Channels.Count)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Deck slot is out of range.");

        var next = Channels.ToArray();
        next[slot] = channel;
        return this with { Channels = next };
    }

    /// <summary>Returns a copy with the crossfader position clamped to 0..1.</summary>
    public MixerState WithCrossfader(double position)
        => this with { Crossfader = Math.Clamp(position, 0.0, 1.0) };

    /// <summary>Returns a copy with the headphone-cue bus state replaced.</summary>
    public MixerState WithCueBus(CueBusState cueBus)
    {
        ArgumentNullException.ThrowIfNull(cueBus);
        return this with { CueBus = cueBus };
    }
}
