namespace Liveolator.Core.Beat;

/// <summary>
/// A consumer ticked by the <see cref="MasterClockBridge"/> on the same dedicated pump cadence that
/// drives sync correction and the shared clock (doc 03). Riding this one tick — instead of owning a
/// timer — is how time-driven automation (auto-mix) stays on the single beat mechanism the whole
/// application shares.
/// </summary>
public interface IMasterClockTickListener
{
    /// <summary>One pump tick, after sync correction ran and the shared clock was updated.</summary>
    void OnMasterClockTick(long hostTimeTicks);
}
