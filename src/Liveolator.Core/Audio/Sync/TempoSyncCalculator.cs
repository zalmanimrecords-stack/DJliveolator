namespace Liveolator.Core.Audio.Sync;

/// <summary>
/// The pure tempo-match (beatmatch) calculation behind Sync Lock (doc 11): given the sync leader's
/// tempo and a follower deck's natural tempo, returns the playback-rate multiplier that makes the
/// follower's tempo match the leader's. Folds ½×/2× so a 70 BPM track follows a 140 BPM leader at a
/// rate near 1.0 (beats align every other leader beat) instead of an out-of-range doubling.
/// </summary>
/// <remarks>
/// Pure and hardware-free so it unit-tests under xUnit; the engine multiplies a deck's natural sample
/// rate by this factor. Phase alignment (Quantize) is a separate, deferred concern (doc 11) — this
/// only matches tempo.
/// </remarks>
public static class TempoSyncCalculator
{
    // The follower folds to the octave of the leader nearest 1.0: a ratio at or above √2 halves, a
    // ratio below √½ doubles. √2 is the geometric midpoint between an octave and its double, so the
    // chosen octave is always the closest tempo relationship to the leader.
    private static readonly double UpperFold = Math.Sqrt(2.0);
    private static readonly double LowerFold = Math.Sqrt(0.5);

    /// <summary>
    /// Rate multiplier for a follower deck so its tempo matches <paramref name="leaderBpm"/>. Returns
    /// 1.0 (no change) when either tempo is non-positive — there is nothing to match against.
    /// </summary>
    /// <param name="leaderBpm">The sync leader's current tempo (BPM).</param>
    /// <param name="followerBaseBpm">The follower's natural (un-pitched) tempo (BPM).</param>
    public static double RateFor(double leaderBpm, double followerBaseBpm)
    {
        if (leaderBpm <= 0.0 || followerBaseBpm <= 0.0)
            return 1.0;

        double ratio = leaderBpm / followerBaseBpm;
        while (ratio >= UpperFold)
            ratio /= 2.0;
        while (ratio < LowerFold)
            ratio *= 2.0;
        return ratio;
    }

    /// <summary>
    /// The beatmatch rate subject to a maximum stretch ceiling. SYNC may pull a deck beyond the manual
    /// pitch-fader range (key-lock preserves the pitch), but only so far: past <paramref name="maxStretch"/>
    /// (a fraction, e.g. 0.15 = ±15%) the two tracks are too far apart to beatmatch cleanly. Rather than
    /// command a wildly out-of-range rate (a chipmunk pitch jump), this reports <c>WithinRange=false</c> and
    /// holds unity so the caller can surface a "can't sync" state instead of engaging a bad lock.
    /// </summary>
    /// <param name="leaderBpm">The sync leader's current tempo (BPM).</param>
    /// <param name="followerBaseBpm">The follower's natural (un-pitched) tempo (BPM).</param>
    /// <param name="maxStretch">Maximum |rate − 1| the sync is allowed to apply (e.g. 0.15 for ±15%).</param>
    public static SyncRate RateWithin(double leaderBpm, double followerBaseBpm, double maxStretch)
    {
        if (leaderBpm <= 0.0 || followerBaseBpm <= 0.0)
            return new SyncRate(1.0, WithinRange: true);

        double rate = RateFor(leaderBpm, followerBaseBpm);
        bool within = Math.Abs(rate - 1.0) <= Math.Abs(maxStretch) + 1e-9;
        return within ? new SyncRate(rate, true) : new SyncRate(1.0, false);
    }
}

/// <summary>
/// A beatmatch rate decision: the rate to apply and whether the tempo gap was inside the allowed sync
/// stretch. When <see cref="WithinRange"/> is false the gap was too wide to beatmatch and <see cref="Rate"/>
/// is unity (no stretch applied) — the caller should report "can't sync" rather than lock.
/// </summary>
public readonly record struct SyncRate(double Rate, bool WithinRange);
