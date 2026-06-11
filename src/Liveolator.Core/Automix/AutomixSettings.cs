namespace Liveolator.Core.Automix;

/// <summary>
/// Tunables for the auto-mix engine. Defaults follow the advisor spec (doc 11 Auto-Mix): 4/4 bars,
/// a 4-bar sync-lock timeout (a pairing that cannot lock aborts before anything is audible), a
/// 2-bar safety tail before the outgoing track's end, and the engine's ±8% pitch range as the
/// tempo-gap refusal gate (no keylock yet — large stretches would also shift pitch audibly).
/// </summary>
/// <param name="BeatsPerBar">Beats per bar for all bar math (4/4 assumed until meter analysis exists).</param>
/// <param name="SyncTimeoutBars">Bars to wait for the incoming deck to report Locked before aborting.</param>
/// <param name="LockConfirmTicks">Consecutive Locked ticks required before the blend may begin.</param>
/// <param name="SafetyTailBars">Bars the transition must finish before the outgoing track ends.</param>
/// <param name="MaxRateDeviation">Refusal gate: the folded tempo-match rate must be within 1 ± this.</param>
/// <param name="DispatchEpsilon">Minimum parameter change worth dispatching (keeps action traffic lean).</param>
/// <param name="MinIncomingHeadroomBars">Bars the incoming track must still have after the blend completes.</param>
public sealed record AutomixSettings(
    int BeatsPerBar = 4,
    int SyncTimeoutBars = 4,
    int LockConfirmTicks = 2,
    int SafetyTailBars = 2,
    double MaxRateDeviation = 0.08,
    double DispatchEpsilon = 0.005,
    int MinIncomingHeadroomBars = 8)
{
    /// <summary>The advisor-spec defaults.</summary>
    public static AutomixSettings Default { get; } = new();
}
