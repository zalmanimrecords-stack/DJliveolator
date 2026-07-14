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

        // Both distances are fractions of *that deck's own* beat, so they can only be subtracted on a
        // common grid. Express the follower's phase in LEADER beats first (a follower beat spans
        // leaderBpm/followerBpm leader beats); otherwise a half/double-tempo pairing (e.g. 70 vs 140)
        // subtracts fractions of unequal-length beats and can lock the follower onto the leader's
        // OFF-beat (doc 27 B2). When the tempos are equal the factor is 1 and this is the original math.
        double followerInLeaderBeats = followerDistance * (leader.Bpm / follower.Bpm);

        // The follower must reach the leader's beat-distance. The signed error wrapped to (-0.5, 0.5]
        // leader-beats picks the shortest correction onto the nearest leader beat (advance vs. rewind).
        double errorBeats = leaderDistance - followerInLeaderBeats;
        return errorBeats - Math.Round(errorBeats); // wrap to (-0.5, 0.5]
    }

    /// <summary>
    /// Seconds to nudge the <paramref name="follower"/> playhead so its beat phase aligns with the
    /// <paramref name="leader"/>'s. Positive = move the playhead forward, negative = back; the result is
    /// the shortest correction onto the nearest leader beat. Returns 0 when either tempo is non-positive
    /// (no shared grid to align to).
    /// </summary>
    /// <param name="follower">The deck being snapped (position/anchor/effective BPM).</param>
    /// <param name="leader">The sync leader defining the target grid (position/anchor/effective BPM).</param>
    public static double PhaseNudgeSeconds(DeckPhase follower, DeckPhase leader)
    {
        if (follower.Bpm <= 0.0 || leader.Bpm <= 0.0)
            return 0.0;

        // BeatPhaseError is in LEADER beats (the follower aligns to the leader's grid), so convert with
        // the leader's beat duration. Equal tempo → leader beat == follower beat: the original behaviour.
        double leaderBeatSeconds = 60.0 / leader.Bpm;
        return BeatPhaseError(follower, leader) * leaderBeatSeconds;
    }

    /// <summary>
    /// The signed BAR-phase error between two decks, in bars wrapped to (-0.5, 0.5]. Beat-phase
    /// alignment can lock the follower's beat 3 onto the leader's downbeat — audibly in sync but
    /// musically a bar off, which a bar-accurate transition cannot tolerate (doc 27 known gap).
    /// Folding on the bar grid instead picks the shortest correction onto the nearest leader
    /// DOWNBEAT. Returns 0 when either tempo is non-positive or
    /// <paramref name="beatsPerBar"/> is not positive.
    /// </summary>
    /// <remarks>
    /// Implemented on the same wrapped-phase math as <see cref="BeatPhaseError"/> by treating the bar
    /// as the grid unit (tempo ÷ beats-per-bar), so beat- and bar-alignment cannot diverge. The
    /// first-beat anchor is taken as bar origin; analysis measures a within-beat anchor, not a true
    /// musical downbeat, so this guarantees a CONSISTENT bar grid between the decks, not that bar 1
    /// of the song lands on bar 1 — an honest v1 limitation (doc 16 phrase cues are the upgrade).
    /// </remarks>
    public static double BarPhaseError(DeckPhase follower, DeckPhase leader, int beatsPerBar)
    {
        if (beatsPerBar <= 0)
            return 0.0;
        return BeatPhaseError(
            follower with { Bpm = follower.Bpm / beatsPerBar },
            leader with { Bpm = leader.Bpm / beatsPerBar });
    }

    /// <summary>
    /// Seconds to nudge the <paramref name="follower"/> playhead so its BAR phase aligns with the
    /// <paramref name="leader"/>'s — the shortest correction onto the nearest leader downbeat.
    /// PRE-FADE ONLY: the correction spans up to half a BAR (±2 beats at 4/4), so applying it to a deck
    /// already audible in the mix is an audible skip — every caller must gate on the deck NOT playing
    /// (see <c>TwoDeckBassEngine.PhaseAlignToLeader</c>) and fall back to the ±half-beat
    /// <see cref="PhaseNudgeSeconds"/> otherwise. The continuous <c>PhaseLockController</c> stays
    /// beat-based, which preserves bar alignment once established.
    /// Returns 0 when either tempo or <paramref name="beatsPerBar"/> is not positive.
    /// </summary>
    public static double BarPhaseNudgeSeconds(DeckPhase follower, DeckPhase leader, int beatsPerBar)
    {
        if (follower.Bpm <= 0.0 || leader.Bpm <= 0.0 || beatsPerBar <= 0)
            return 0.0;

        double leaderBarSeconds = beatsPerBar * (60.0 / leader.Bpm);
        return BarPhaseError(follower, leader, beatsPerBar) * leaderBarSeconds;
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
