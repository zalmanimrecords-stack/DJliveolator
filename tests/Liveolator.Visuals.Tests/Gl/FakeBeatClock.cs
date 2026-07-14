using Liveolator.Core.Beat;

namespace Liveolator.Visuals.Tests.Gl;

/// <summary>A test double whose <see cref="Current"/> snapshot can be set directly.</summary>
internal sealed class FakeBeatClock : IBeatClock
{
    public BeatClockState Current { get; set; } = BeatClockState.Idle;

    public event EventHandler<BeatClockState>? StateChanged;

    public void Publish(BeatClockState state)
    {
        Current = state;
        StateChanged?.Invoke(this, state);
    }
}
