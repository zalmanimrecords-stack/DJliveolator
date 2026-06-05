namespace Liveolator.Core.Beat;

/// <summary>
/// The live beat clock: exposes the current state and notifies consumers (visuals, playlist, UI)
/// as it evolves, at least once per beat (doc 03).
/// </summary>
public interface IBeatClock
{
    /// <summary>The latest published state.</summary>
    BeatClockState Current { get; }

    /// <summary>Raised when the state changes; fires at least on every beat boundary.</summary>
    event EventHandler<BeatClockState>? StateChanged;
}
