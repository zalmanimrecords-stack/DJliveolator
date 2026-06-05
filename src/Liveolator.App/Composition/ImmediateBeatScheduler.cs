using Liveolator.Core.Beat;

namespace Liveolator.App.Composition;

/// <summary>
/// Interim <see cref="IBeatScheduler"/> that fires the action immediately. Until a clock-driven
/// scheduler (resolving the fire time against the shared beat timeline via <see cref="BeatQuantizer"/>
/// and a timer) is wired, quantized deferrals such as <c>PlaylistSkipOnNextBar</c> run now rather than
/// snapping to the next bar. This keeps the live queue editable today; bar-accurate timing is a later
/// increment (doc 03/09).
/// </summary>
internal sealed class ImmediateBeatScheduler : IBeatScheduler
{
    public void Schedule(Quantize when, int everyN, Action onFire)
    {
        ArgumentNullException.ThrowIfNull(onFire);
        onFire();
    }
}
