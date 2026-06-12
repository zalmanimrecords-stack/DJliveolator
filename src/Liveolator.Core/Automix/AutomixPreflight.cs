using Liveolator.Core.Audio.Sync;

namespace Liveolator.Core.Automix;

/// <summary>
/// The go/no-go gate run before any auto-mix transition starts (advisor spec S3/S4): every check is
/// pure and produces a typed refusal, so a transition that cannot be executed safely is rejected
/// BEFORE anything is audible — and the reason reaches the UI/log. Refuse, never guess.
/// </summary>
public static class AutomixPreflight
{
    /// <summary>
    /// Resolve the plan for a transition from <paramref name="from"/> to <paramref name="to"/>.
    /// Degrades (duration auto-shortened to fit the outgoing track) where the degraded transition
    /// is still safe; refuses where it is not.
    /// </summary>
    public static AutomixPlan Plan(
        AutomixDeckSnapshot from,
        int fromSlot,
        AutomixDeckSnapshot to,
        int toSlot,
        int requestedBars,
        AutomixSettings settings)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(settings);

        if (!from.IsLoaded || !from.IsPlaying)
            return AutomixPlan.Refused(AutomixRefusal.NothingPlaying);
        if (!to.IsLoaded)
            return AutomixPlan.Refused(AutomixRefusal.IncomingNotLoaded);

        // Tempo gates: both BPMs must be known, and even the octave-folded match rate must sit inside
        // the engine's pitch range — beyond it the blend would be out of range (and audibly chipmunked
        // until keylock lands).
        double leaderBpm = from.EffectiveBpm > 0.0 ? from.EffectiveBpm : from.BaseBpm;
        if (leaderBpm <= 0.0 || to.BaseBpm <= 0.0)
            return AutomixPlan.Refused(AutomixRefusal.TempoUnknown);
        double foldedRate = TempoSyncCalculator.RateFor(leaderBpm, to.BaseBpm);
        if (Math.Abs(foldedRate - 1.0) > settings.MaxRateDeviation)
            return AutomixPlan.Refused(AutomixRefusal.TempoGapTooLarge);

        // Read-ahead fit: the blend must COMPLETE with a safety tail before the outgoing track ends.
        double remaining = Math.Max(0.0, from.LengthSeconds - from.PositionSeconds);
        int plannedBars = AutomixPlacement.FitBars(
            requestedBars, remaining, leaderBpm, settings.BeatsPerBar, settings.SafetyTailBars);
        if (plannedBars <= 0)
            return AutomixPlan.Refused(AutomixRefusal.NotEnoughTimeLeft);

        // The incoming track must carry the floor after the blend, not end right behind it. Media-time
        // bars are measured at the incoming track's base BPM (its beat spacing in source seconds; the
        // octave-folded match rate keeps that approximation within a factor the headroom absorbs).
        double mixIn = AutomixPlacement.MixInSeconds(to);
        double toBarSeconds = settings.BeatsPerBar * (60.0 / to.BaseBpm);
        double neededSeconds = (plannedBars + settings.MinIncomingHeadroomBars) * toBarSeconds;
        if (to.LengthSeconds - mixIn < neededSeconds)
            return AutomixPlan.Refused(AutomixRefusal.IncomingTooShort);

        return new AutomixPlan(AutomixRefusal.None, fromSlot, toSlot, plannedBars, mixIn);
    }
}
