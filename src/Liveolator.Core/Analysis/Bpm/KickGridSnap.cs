namespace Liveolator.Core.Analysis.Bpm;

/// <summary>
/// Snaps a deck's beat-grid phase onto the real kick nearest the playhead (the "SET PHASE" action and the
/// one-shot SYNC auto-align, doc 11). Given the analyzed kick strike times (<see cref="KickOnsetPicker"/>)
/// and where the playhead sits, it returns the first-beat anchor that puts a grid line exactly on that
/// kick — so alignment lands on the detected transient, not on wherever the playhead happened to stop, and
/// not on a global anchor a drifting tempo has pulled off the local kick. Pure and hardware-free (doc 16).
/// </summary>
public static class KickGridSnap
{
    /// <summary>
    /// The first-beat anchor (seconds, in [0, 60/bpm)) that lands a beat line on the kick nearest
    /// <paramref name="playheadSeconds"/>. Returns <paramref name="fallbackAnchor"/> when there are no
    /// kicks or the tempo is non-positive (nothing to snap to) — the caller's existing behaviour stands.
    /// </summary>
    /// <param name="kickOnsetsSeconds">Kick strike times (any order), from analysis.</param>
    /// <param name="playheadSeconds">Current playhead position from track start (seconds).</param>
    /// <param name="bpm">The deck's analyzed base tempo (BPM).</param>
    /// <param name="fallbackAnchor">Anchor to return when no snap is possible (default 0).</param>
    public static double NearestKickAnchor(
        IReadOnlyList<double> kickOnsetsSeconds, double playheadSeconds, double bpm, double fallbackAnchor = 0.0)
    {
        ArgumentNullException.ThrowIfNull(kickOnsetsSeconds);
        if (bpm <= 0.0 || kickOnsetsSeconds.Count == 0)
            return fallbackAnchor;

        double nearest = kickOnsetsSeconds[0];
        double bestDistance = Math.Abs(nearest - playheadSeconds);
        for (int i = 1; i < kickOnsetsSeconds.Count; i++)
        {
            double distance = Math.Abs(kickOnsetsSeconds[i] - playheadSeconds);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = kickOnsetsSeconds[i];
            }
        }

        // Fold the kick's absolute time into one beat: every grid line then sits on this kick's phase.
        double beatSeconds = 60.0 / bpm;
        double anchor = nearest % beatSeconds;
        return anchor < 0.0 ? anchor + beatSeconds : anchor;
    }
}
