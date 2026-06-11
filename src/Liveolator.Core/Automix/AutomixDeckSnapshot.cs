using Liveolator.Core.Audio.Sync;

namespace Liveolator.Core.Automix;

/// <summary>
/// A read-only snapshot of one deck as the auto-mix engine sees it — everything preflight, placement,
/// and the transition state machine need, with no live engine reference. Times in seconds from the
/// track start; unknown tempo/anchor follow the engine conventions (0 = unknown).
/// </summary>
/// <param name="IsLoaded">A track is loaded in this slot.</param>
/// <param name="IsPlaying">The slot is currently playing.</param>
/// <param name="BaseBpm">Analyzed natural tempo; 0 when unknown.</param>
/// <param name="EffectiveBpm">Audible tempo after pitch/sync rate; 0 when unknown.</param>
/// <param name="FirstBeatSeconds">First-beat (grid) anchor; 0 when unknown (or genuinely at 0).</param>
/// <param name="PositionSeconds">Current playhead in seconds.</param>
/// <param name="LengthSeconds">Track length in seconds; 0 when nothing is loaded.</param>
/// <param name="SyncState">The deck's continuous beat-lock state.</param>
/// <param name="SyncLocked">True while the deck's SYNC latch is engaged.</param>
public sealed record AutomixDeckSnapshot(
    bool IsLoaded,
    bool IsPlaying,
    double BaseBpm,
    double EffectiveBpm,
    double FirstBeatSeconds,
    double PositionSeconds,
    double LengthSeconds,
    SyncLockState SyncState,
    bool SyncLocked)
{
    /// <summary>True when the deck has a usable beat-grid anchor (doc 16 analysis recorded one).</summary>
    public bool HasGrid => FirstBeatSeconds > 0.0 && BaseBpm > 0.0;
}
