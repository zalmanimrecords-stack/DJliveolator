namespace Liveolator.Core.Audio.Sync;

/// <summary>
/// The pure phase-match calculation behind Quantize (doc 11): tempo sync alone matches speed but does
/// not align beats, so Quantize snaps a follower deck's beat phase onto the sync leader's grid. Given
/// each deck's first-beat anchor (the downbeat offset measured by analysis), effective tempo, and
/// current playhead position, this returns the signed seconds the follower's playhead must move so its
/// nearest beat lines up with the leader's — the smallest correction in either direction.
/// </summary>
/// <remarks>
/// Pure and hardware-free so it unit-tests under xUnit; the engine applies the returned nudge by
/// seeking the follower deck. Tempo match (rate) is a separate concern (<see cref="TempoSyncCalculator"/>);
/// this only aligns phase and assumes the two decks already share a tempo.
/// </remarks>
public static class PhaseAlignmentCalculator
{
    /// <summary>
    /// A deck's beat-distance: how far the playhead sits past its last beat boundary, in [0,1) beats
    /// (doc 03 beat-distance). 0 = exactly on a beat. Returns 0 when the tempo is non-positive (no grid
    /// to measure against).
    /// </summary>
    /// <param name="positionSeconds">Current playhead position from the track start (seconds).</param>
    /// <param name="firstBeatSeconds">The track's first-beat (downbeat) anchor from analysis (seconds).</param>
    /// <param name="bpm">The deck's effective tempo (BPM) — its base BPM scaled by any pitch/sync rate.</param>
    public static double BeatDistance(double positionSeconds, double firstBeatSeconds, double bpm)
    {
        if (bpm <= 0.0)
            return 0.0;

        double beatSeconds = 60.0 / bpm;
        double beats = (positionSeconds - firstBeatSeconds) / beatSeconds;
        double frac = beats - Math.Floor(beats); // [0,1), tolerant of a negative offset before the anchor
        return frac;
    }

    /// <summary>
    /// The signed beat-phase error between two decks, in beats wrapped to (-0.5, 0.5]: how far the
    /// <paramref name="follower"/> must move to align its beat phase with the <paramref name="leader"/>'s,
    /// in the shorter direction. Positive = the follower is behind the leader's beat (must advance);
    /// negative = ahead (must rewind). Returns 0 when either tempo is non-positive (no shared grid).
    /// </summary>
    /// <remarks>
    /// The single source of phase-error math: the one-shot Quantize snap (<see cref="PhaseNudgeSeconds"/>)
    /// converts this to seconds, and the continuous <c>PhaseLockController</c> feeds it straight into its
    /// proportional correction. Keeping both on one wrapped-beats definition keeps snap and lock consistent.
    /// </remarks>
    /// <param name="follower">The deck whose phase is measured (position/anchor/effective BPM).</param>
    /// <param name="leader">The deck defining the target grid (position/anchor/effective BPM).</param>
    public static double BeatPhaseError(DeckPhase follower, DeckPhase leader)
    {
        if (follower.Bpm <= 0.0 || leader.Bpm <= 0.0)
            return 0.0;

        double followerDistance = BeatDistance(follower.PositionSeconds, follower.FirstBeatSeconds, follower.Bpm);
        double leaderDistance = BeatDistance(leader.PositionSeconds, leader.FirstBeatSeconds, leader.Bpm);

        // The follower must reach the leader's beat-distance. The signed error wrapped to (-0.5, 0.5]
        // picks the shorter direction (advance vs. rewind) to the nearest aligned beat.
        double errorBeats = leaderDistance - followerDistance;
        return errorBeats - Math.Round(errorBeats); // wrap to (-0.5, 0.5]
    }

    /// <summary>
    /// Seconds to nudge the <paramref name="follower"/> playhead so its beat phase aligns with the
    /// <paramref name="leader"/>'s. Positive = move the playhead forward, negative = back; the result is
    /// the shortest correction, always within ±half a follower beat. Returns 0 when either tempo is
    /// non-positive (no shared grid to align to).
    /// </summary>
    /// <param name="follower">The deck being snapped (position/anchor/effective BPM).</param>
    /// <param name="leader">The sync leader defining the target grid (position/anchor/effective BPM).</param>
    public static double PhaseNudgeSeconds(DeckPhase follower, DeckPhase leader)
    {
        if (follower.Bpm <= 0.0 || leader.Bpm <= 0.0)
            return 0.0;

        double followerBeatSeconds = 60.0 / follower.Bpm;
        return BeatPhaseError(follower, leader) * followerBeatSeconds;
    }
}

/// <summary>
/// A deck's phase inputs for alignment: where its playhead is, where its first beat sits, and its
/// effective tempo. All times are seconds from the track start.
/// </summary>
/// <param name="PositionSeconds">Current playhead position from the track start (seconds).</param>
/// <param name="FirstBeatSeconds">The track's first-beat (downbeat) anchor from analysis (seconds).</param>
/// <param name="Bpm">The deck's effective tempo (BPM) — base BPM scaled by any pitch/sync rate.</param>
public readonly record struct DeckPhase(double PositionSeconds, double FirstBeatSeconds, double Bpm);
