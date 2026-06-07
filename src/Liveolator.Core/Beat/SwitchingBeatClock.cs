namespace Liveolator.Core.Beat;

/// <summary>
/// One subscriber-facing <see cref="IBeatClock"/> that re-publishes whichever inner clock is currently
/// selected. Consumers (the visual engine, the Live beat readout) subscribe once and automatically
/// follow the active source as the host loop switches it — the master deck's grid
/// (<see cref="DeckDrivenBeatClock"/>) while a deck is the sync master, the tap/audio clock otherwise.
/// This preserves the product's "one shared clock" contract while the source of truth varies.
/// </summary>
public sealed class SwitchingBeatClock : IBeatClock
{
    private IBeatClock _active;

    /// <param name="initial">The source to forward until <see cref="Select"/> changes it.</param>
    public SwitchingBeatClock(IBeatClock initial)
    {
        _active = initial ?? throw new ArgumentNullException(nameof(initial));
        _active.StateChanged += Forward;
    }

    /// <inheritdoc />
    public BeatClockState Current => _active.Current;

    /// <inheritdoc />
    public event EventHandler<BeatClockState>? StateChanged;

    /// <summary>The currently forwarded source.</summary>
    public IBeatClock Active => _active;

    /// <summary>
    /// Switch the active source. Re-subscribes and republishes the new source's current state so
    /// consumers update on the same tick. No-op when <paramref name="clock"/> is already active.
    /// </summary>
    public void Select(IBeatClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (ReferenceEquals(clock, _active))
            return;

        _active.StateChanged -= Forward;
        _active = clock;
        _active.StateChanged += Forward;
        StateChanged?.Invoke(this, _active.Current);
    }

    private void Forward(object? sender, BeatClockState state) => StateChanged?.Invoke(this, state);
}
