namespace Liveolator.Core.Beat;

/// <summary>
/// The render-loop entry point of a manually-driven beat clock: advancing it to the current host
/// time re-publishes <see cref="BeatClockState"/> so beat/bar phase (and any pulse indicator) move
/// smoothly between taps. Separated from <see cref="IBeatClockControl"/> because this is a frame
/// pump, not a performer command, and from <see cref="IBeatClock"/> because consumers that only read
/// state must not be able to advance the grid. Lets the UI render loop drive the clock without
/// depending on the concrete <see cref="ManualBeatClock"/> (doc 03).
/// </summary>
public interface IManualBeatClockDriver
{
    /// <summary>Advances the clock to <paramref name="hostTimeTicks"/> and republishes its state.</summary>
    void Update(long hostTimeTicks);
}
