namespace Liveolator.Core.Mixer;

/// <summary>
/// The complete, immutable state of the software mixer (doc 11): the crossfader position, the
/// selected crossfader curve, and one <see cref="DeckChannelState"/> per deck slot. Pure data with
/// no behaviour — the math that derives audible gains lives in <see cref="MixerMath"/> and the
/// action wiring in <c>MixerActionHandler</c>. <see cref="DeckCount"/> slots: A = 0 and B = 1 are
/// the live/DJ decks the crossfader blends; C = 2 and D = 3 are the hidden STUDIO decks (per-deck
/// gain only, outside the A/B crossfader). Indexable so callers stay slot-generic.
/// </summary>
/// <param name="Crossfader">Crossfader position 0..1: 0 = full deck A, 1 = full deck B, 0.5 = center.</param>
/// <param name="Curve">Shape of the crossfader transition.</param>
/// <param name="Channels">Per-deck channel strips, indexed by deck slot.</param>
/// <param name="CueBus">The headphone-cue (PFL) bus level and cue/master blend.</param>
/// <param name="CutMode">Mixer-wide EQ cut-depth mode applied to every channel's bands (doc 11).</param>
public sealed record MixerState(
    double Crossfader,
    CrossfaderCurve Curve,
    IReadOnlyList<DeckChannelState> Channels,
    CueBusState CueBus,
    EqCutMode CutMode = EqCutMode.Kill)
{
    /// <summary>Number of deck slots: 2 live (A/B) + 2 hidden STUDIO decks (C/D).</summary>
    public const int DeckCount = 4;

    /// <summary>Deck slot index for deck A (live).</summary>
    public const int DeckA = 0;

    /// <summary>Deck slot index for deck B (live).</summary>
    public const int DeckB = 1;

    /// <summary>Deck slot index for deck C (hidden — STUDIO only).</summary>
    public const int DeckC = 2;

    /// <summary>Deck slot index for deck D (hidden — STUDIO only).</summary>
    public const int DeckD = 3;

    /// <summary>Crossfader centered, smooth curve, every deck at its default channel strip, cue bus default.</summary>
    public static MixerState Default { get; } = new(
        Crossfader: 0.5,
        Curve: CrossfaderCurve.Smooth,
        Channels: Enumerable.Repeat(DeckChannelState.Default, DeckCount).ToArray(),
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

    /// <summary>Returns a copy with the mixer-wide EQ cut-depth mode replaced.</summary>
    public MixerState WithCutMode(EqCutMode cutMode) => this with { CutMode = cutMode };
}
