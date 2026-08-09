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
/// low comes in, crossing at the middle of the blend.</item>
/// </list>
/// One lane per (deck, target) covering the whole set, because the renderer keeps only the last lane it
/// sees for a given pair.
/// </summary>
public static class TransitionAutomation
{
    /// <summary>Points sampled per fade — linear interpolation between them tracks the cosine to ~0.04 dB.</summary>
    private const int FadeSteps = 8;

    /// <summary>How long the low bands take to trade places, centred on the middle of the blend.</summary>
    private const int BassSwapBars = 4;

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

        double swapSeconds = Math.Min(
            BassSwapBars * SetBuildOptions.BarSeconds(tempoBpm),
            windows.Min(w => w.OverlapSeconds) / 2.0);

        var gain = new Dictionary<int, List<AutomationKeyframe>>();
        var low = new Dictionary<int, List<AutomationKeyframe>>();

        foreach (CrossfadeWindow window in windows)
        {
            AddEqualPowerFade(Lane(gain, window.OutSlot), window, fadingIn: false);
            AddEqualPowerFade(Lane(gain, window.InSlot), window, fadingIn: true);

            double middle = window.StartSeconds + (window.OverlapSeconds / 2.0);
            Lane(low, window.OutSlot).Add(new AutomationKeyframe(middle - swapSeconds, EqBands.Unity));
            Lane(low, window.OutSlot).Add(new AutomationKeyframe(middle, BandCut));
            Lane(low, window.InSlot).Add(new AutomationKeyframe(middle, BandCut));
            Lane(low, window.InSlot).Add(new AutomationKeyframe(middle + swapSeconds, EqBands.Unity));
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

    private static List<AutomationKeyframe> Lane(Dictionary<int, List<AutomationKeyframe>> lanes, int slot)
        => lanes.TryGetValue(slot, out List<AutomationKeyframe>? existing)
            ? existing
            : lanes[slot] = new List<AutomationKeyframe>();

    // Lanes are sampled by scanning forward, so the keyframes must be non-decreasing in time. A deck's
    // windows are already added in play order; the sort guards against a caller that isn't.
    private static AutomationLane ToLane(AutomationTarget target, int slot, List<AutomationKeyframe> keyframes)
        => new(target, slot, keyframes.OrderBy(k => k.TimeSeconds).ToArray());
}
