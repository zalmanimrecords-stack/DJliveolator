using Liveolator.Core.Studio;
using Liveolator.Core.Studio.Set;

namespace Liveolator.Core.Tests.Studio.Set;

/// <summary>
/// At 128 BPM a bar is 1.875 s, so the intended 4-bar bass swap is 7.5 s and half of a 30 s blend is 15 s —
/// the full swap fits. These numbers keep the expected keyframe positions readable.
/// </summary>
public sealed class TransitionAutomationTests
{
    private const double TempoBpm = 128.0;
    private const double FullSwapSeconds = 7.5;

    [Fact]
    public void Build_SizesTheBassSwap_PerTransition_NotFromTheShortestInTheSet()
    {
        // A set almost always contains one short join (a hard-ending record, or a low-confidence clamp).
        // Sizing every swap from the shortest one lets that single join shorten the bass trade for the whole
        // set, so the long blends stack two basslines for longer than intended — audible as mud.
        var windows = new[]
        {
            new CrossfadeWindow(OutSlot: 0, InSlot: 1, StartSeconds: 0.0, OverlapSeconds: 30.0),
            new CrossfadeWindow(OutSlot: 1, InSlot: 0, StartSeconds: 100.0, OverlapSeconds: 6.0),
        };

        IReadOnlyList<AutomationLane> lanes = TransitionAutomation.Build(windows, TempoBpm);

        // The long blend's outgoing low starts leaving a full 4 bars before the crossing point at its middle.
        double longMiddle = 0.0 + (30.0 / 2.0);
        AutomationLane outgoingLow = Assert.Single(lanes, l => l.Target == AutomationTarget.EqLow && l.DeckSlot == 0);
        Assert.Contains(
            outgoingLow.Keyframes,
            k => Math.Abs(k.TimeSeconds - (longMiddle - FullSwapSeconds)) < 1e-6);
    }

    [Fact]
    public void Build_KeepsTheBassSwap_InsideAShortBlend()
    {
        // The clamp itself is right: the swap can never run past the blend it lives in, or the outgoing low
        // would start dropping before the incoming record is even audible.
        var windows = new[] { new CrossfadeWindow(0, 1, StartSeconds: 0.0, OverlapSeconds: 6.0) };

        IReadOnlyList<AutomationLane> lanes = TransitionAutomation.Build(windows, TempoBpm);

        AutomationLane outgoingLow = Assert.Single(lanes, l => l.Target == AutomationTarget.EqLow && l.DeckSlot == 0);
        Assert.All(outgoingLow.Keyframes, k => Assert.True(k.TimeSeconds >= 0.0, "the swap escaped its blend"));
    }
}
