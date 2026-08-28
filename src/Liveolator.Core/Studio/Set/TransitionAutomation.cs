using Liveolator.Core.Mixer;

namespace Liveolator.Core.Studio.Set;

/// <summary>One crossfade as the automation builder sees it: which decks, when, and for how long.</summary>
public sealed record CrossfadeWindow(int OutSlot, int InSlot, double StartSeconds, double OverlapSeconds);

/// <summary>
/// Builds the deck automation that turns overlapping clips into an actual mix. Two things happen at every
/// join, and both are what separates a DJ from a robot:
/// <list type="bullet">
/// <item><b>Equal-power crossfade.</b> Two uncorrelated records at half amplitude each sum to −3 dB, so a
/// straight linear fade dips in the middle of every transition. A cosine/sine pair holds the sum constant.</item>
/// <item><b>Bass swap.</b> Two kicks and two basslines stacked for half a minute is mud, and it is the
/// single loudest tell that nobody was actually mixing. The outgoing low is pulled out as the incoming
/// low comes in — both at once, crossing on the middle bar line of the blend.</item>
/// </list>
/// One lane per (deck, target) covering the whole set, because the renderer keeps only the last lane it
/// sees for a given pair.
/// </summary>
public static class TransitionAutomation
{
    /// <summary>Points sampled per fade — linear interpolation between them tracks the cosine to ~0.04 dB.</summary>
    private const int FadeSteps = 8;

    /// <summary>
    /// Bars of blend per bar of bass swap: an 8-bar blend trades its lows over 1 bar, 16 over 2, 32 over 4.
    /// Owner's call (2026-08-28) over a fixed 2 bars — a longer blend earns a proportionally longer hand-over.
    /// <see cref="MaxBassSwapBars"/> is what answers the objection that made 2 bars fixed look safer: without a
    /// ceiling a 32-bar psy blend would spend 16 of them with both records' low ends compromised.
    /// </summary>
    private const double BlendBarsPerSwapBar = 8.0;

    /// <summary>Shortest hand-over: below a bar the trade stops reading as a move on a downbeat.</summary>
    private const double MinBassSwapBars = 1.0;

    /// <summary>Longest hand-over, however long the blend runs.</summary>
    private const double MaxBassSwapBars = 4.0;

    /// <summary>Full cut on an EQ band.</summary>
    private const double BandCut = 0.0;

    /// <summary>
    /// The gain and low-EQ lanes for <paramref name="windows"/> at <paramref name="tempoBpm"/>. Decks with
    /// no transitions get no lane, which the renderer reads as unity gain and flat EQ.
    /// </summary>
    public static IReadOnlyList<AutomationLane> Build(IReadOnlyList<CrossfadeWindow> windows, double tempoBpm)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (windows.Count == 0)
            return Array.Empty<AutomationLane>();

        double barSeconds = SetBuildOptions.BarSeconds(tempoBpm);

        var gain = new Dictionary<int, List<AutomationKeyframe>>();
        var low = new Dictionary<int, List<AutomationKeyframe>>();

        foreach (CrossfadeWindow window in windows)
        {
            AddEqualPowerFade(Lane(gain, window.OutSlot), window, fadingIn: false);
            AddEqualPowerFade(Lane(gain, window.InSlot), window, fadingIn: true);

            // Sized per blend, not once for the set: the swap must stay inside the blend it lives in, but
            // taking the shortest blend in the set as the bound would let one hard-ending record shorten the
            // bass trade everywhere, leaving two basslines stacked through every long blend.
            double swapSeconds = SwapSeconds(window.OverlapSeconds, barSeconds);
            double middle = window.StartSeconds + (window.OverlapSeconds / 2.0);
            AddBassSwapRamp(Lane(low, window.OutSlot), middle, swapSeconds, handingOver: true);
            AddBassSwapRamp(Lane(low, window.InSlot), middle, swapSeconds, handingOver: false);
        }

        return gain.Select(e => ToLane(AutomationTarget.DeckGain, e.Key, e.Value))
            .Concat(low.Select(e => ToLane(AutomationTarget.EqLow, e.Key, e.Value)))
            .ToArray();
    }

    // cos/sin over the blend: the two curves square-sum to 1, so the mix holds its level through the middle.
    private static void AddEqualPowerFade(List<AutomationKeyframe> keyframes, CrossfadeWindow window, bool fadingIn)
    {
        for (int step = 0; step <= FadeSteps; step++)
        {
            double progress = (double)step / FadeSteps;
            double angle = progress * Math.PI / 2.0;
            keyframes.Add(new AutomationKeyframe(
                window.StartSeconds + (progress * window.OverlapSeconds),
                fadingIn ? Math.Sin(angle) : Math.Cos(angle)));
        }
    }

    // The two lows trade over ONE window centred on the crossing, not one after the other. The defect this
    // replaces: the outgoing low reached full cut at the exact instant the incoming low began climbing out of
    // it, so the mix's low band was a V bottoming at NOTHING on every join — eight bars wide at the old 4-bar
    // setting, and present on every join of a set that was otherwise judged excellent.
    //
    // Deliberately NOT equal power, unlike AddEqualPowerFade above. These keyframes are EQ knob positions, and
    // below unity the knob is linear in dB (MixerMath maps the cut half onto a 24 dB range), so holding the
    // summed low band at full level would need both knobs sitting around -3 dB — both basslines still
    // essentially present, which is the mud the bass swap exists to remove. A cos/sin pair over the knob
    // instead leaves each side 7.0 dB down where they cross, so the summed low band dips 4.0 dB at its worst
    // and never leaves, while neither record ever holds the floor alongside the other.
    private static void AddBassSwapRamp(
        List<AutomationKeyframe> keyframes, double crossingSeconds, double swapSeconds, bool handingOver)
    {
        double start = crossingSeconds - (swapSeconds / 2.0);
        for (int step = 0; step <= FadeSteps; step++)
        {
            double progress = (double)step / FadeSteps;
            double angle = progress * Math.PI / 2.0;
            double level = handingOver ? Math.Cos(angle) : Math.Sin(angle);

            // The cut end must land exactly on BandCut: the mixer's kill only reaches silence at the very
            // bottom of the knob, so cos(pi/2)'s 6e-17 would leave the outgoing low 24 dB down but still
            // there — two basslines, quietly.
            keyframes.Add(new AutomationKeyframe(
                start + (progress * swapSeconds),
                level < 1e-12 ? BandCut : EqBands.Unity * level));
        }
    }

    // Derived from the blend, then clamped: never longer than half the blend it lives in, so the trade cannot
    // begin before the incoming record is audible.
    private static double SwapSeconds(double overlapSeconds, double barSeconds)
    {
        if (barSeconds <= 0.0 || overlapSeconds <= 0.0)
            return 0.0;

        double swapBars = Math.Clamp(
            overlapSeconds / barSeconds / BlendBarsPerSwapBar, MinBassSwapBars, MaxBassSwapBars);
        return Math.Min(swapBars * barSeconds, overlapSeconds / 2.0);
    }

    private static List<AutomationKeyframe> Lane(Dictionary<int, List<AutomationKeyframe>> lanes, int slot)
        => lanes.TryGetValue(slot, out List<AutomationKeyframe>? existing)
            ? existing
            : lanes[slot] = new List<AutomationKeyframe>();

    // Lanes are sampled by scanning forward, so the keyframes must be non-decreasing in time. A deck's
    // windows are already added in play order; the sort guards against a caller that isn't.
    private static AutomationLane ToLane(AutomationTarget target, int slot, List<AutomationKeyframe> keyframes)
        => new(target, slot, keyframes.OrderBy(k => k.TimeSeconds).ToArray());
}
