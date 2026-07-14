using Liveolator.Core.Beat;

namespace Liveolator.Core.Tests.Playlist;

/// <summary>A scheduler that fires the action immediately and records how it was scheduled.</summary>
internal sealed class ImmediateBeatScheduler : IBeatScheduler
{
    public Quantize? LastWhen { get; private set; }

    public int LastEveryN { get; private set; }

    public int ScheduleCount { get; private set; }

    public void Schedule(Quantize when, int everyN, Action onFire)
    {
        LastWhen = when;
        LastEveryN = everyN;
        ScheduleCount++;
        onFire();
    }
}
