namespace Liveolator.Core.Automix;

/// <summary>
/// The read-ahead placement math (doc 11 Auto-Mix v1): where the incoming track enters and how long
/// a transition actually fits before the outgoing track runs out. Pure — uses only the
/// already-analyzed BPM, first-beat anchor, position, and duration. Phrase/energy outro detection is
/// the documented v2 upgrade (doc 16 IntroEnd/OutroStart are still null today).
/// </summary>
public static class AutomixPlacement
{
    /// <summary>
    /// The incoming deck's mix-in point: its first-beat anchor when analysis recorded one, else the
    /// track start. (Hot-cue-1 and silence-detected IntroStart take priority once their positions are
    /// readable through the deck seam — doc 11 lists the full priority ladder.)
    /// </summary>
    public static double MixInSeconds(AutomixDeckSnapshot incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        return incoming.FirstBeatSeconds > 0.0 ? incoming.FirstBeatSeconds : 0.0;
    }

    /// <summary>
    /// The largest duration detent ≤ <paramref name="requestedBars"/> that COMPLETES at least
    /// <paramref name="safetyTailBars"/> before the outgoing track ends, or 0 when even the shortest
    /// detent does not fit (the caller refuses — an automated blend must never race the end of file).
    /// </summary>
    /// <param name="requestedBars">The knob-selected transition length.</param>
    /// <param name="outgoingRemainingSeconds">Seconds of material left on the outgoing deck.</param>
    /// <param name="outgoingEffectiveBpm">The outgoing deck's audible tempo (sets bar length in wall time).</param>
    /// <param name="beatsPerBar">Beats per bar.</param>
    /// <param name="safetyTailBars">Bars of margin to keep before the track end.</param>
    public static int FitBars(
        int requestedBars,
        double outgoingRemainingSeconds,
        double outgoingEffectiveBpm,
        int beatsPerBar,
        int safetyTailBars)
    {
        if (outgoingEffectiveBpm <= 0.0 || beatsPerBar <= 0 || outgoingRemainingSeconds <= 0.0)
            return 0;

        double barSeconds = beatsPerBar * (60.0 / outgoingEffectiveBpm);
        for (int i = AutomixDurationKnob.DetentBars.Count - 1; i >= 0; i--)
        {
            int bars = AutomixDurationKnob.DetentBars[i];
            if (bars > requestedBars)
                continue;
            if ((bars + safetyTailBars) * barSeconds <= outgoingRemainingSeconds)
                return bars;
        }
        return 0;
    }
}
