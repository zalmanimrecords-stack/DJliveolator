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
}
