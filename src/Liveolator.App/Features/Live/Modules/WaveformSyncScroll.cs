using System;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// The render-time beat-phase lock for a synced deck's waveform. When a follower is Sync-Locked to a master
/// the two waveforms must scroll TOGETHER — same speed (the playback-time zoom already gives that) AND the
/// grids aligned. This computes the small progress-fraction to add to the follower's playhead so its beat
/// grid lines up with the master's at the needle; because both windows scroll at the same pixels-per-beat,
/// aligning at the needle aligns the whole visible window. The shift wraps to +/- half a beat (the NEAREST
/// aligned beat, never a jump), so when the engine already phase-locks the audio the shift is ~0 (faithful),
/// and when it doesn't the visual still shows the locked grid the DJ expects. Pure — unit-tests without a render.
/// </summary>
internal static class WaveformSyncScroll
{
    /// <summary>
    /// Progress-fraction to ADD to <paramref name="followerProgress"/> so the follower's beat grid aligns to
    /// the master's at the playhead. 0 when any tempo/duration is unknown (nothing to align against).
    /// </summary>
    public static double FollowerOffset(
        double masterProgress, double masterDuration, double masterFirstBeat, double masterBaseBpm,
        double followerProgress, double followerDuration, double followerFirstBeat, double followerBaseBpm)
    {
        if (masterBaseBpm <= 0.0 || masterDuration <= 0.0 || followerBaseBpm <= 0.0 || followerDuration <= 0.0)
            return 0.0;

        double masterBeatPhase = Frac((masterProgress * masterDuration - masterFirstBeat) * masterBaseBpm / 60.0);
        double followerBeatPhase = Frac((followerProgress * followerDuration - followerFirstBeat) * followerBaseBpm / 60.0);

        double shiftBeats = WrapToHalf(masterBeatPhase - followerBeatPhase); // (-0.5, 0.5]
        double shiftSeconds = shiftBeats * 60.0 / followerBaseBpm;
        return shiftSeconds / followerDuration;
    }

    private static double Frac(double x) => x - Math.Floor(x);

    // Wrap a beat delta into (-0.5, 0.5] — the shortest move onto the aligned beat, never the long way around.
    private static double WrapToHalf(double x)
    {
        double w = x - Math.Floor(x); // [0, 1)
        return w > 0.5 ? w - 1.0 : w;
    }
}
