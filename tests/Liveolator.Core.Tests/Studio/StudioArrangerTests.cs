using Liveolator.Core.Actions;
using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.Core.Tests.Studio;

public class StudioArrangerTests
{
    private const double Tol = 1e-9;

    private static StudioClip Clip(int deck, double start, double? lengthSeconds)
        => new(deck, $"/m/deck{deck}.wav", start, TimeSpan.Zero,
            lengthSeconds is { } l ? TimeSpan.FromSeconds(l) : null);

    // --- clip events ---

    [Fact]
    public void ClipEventsBetween_EmitsStartAndStop_InOrder()
    {
        var project = new StudioProject("p", 120, new[] { Clip(2, start: 8, lengthSeconds: 30) },
            Array.Empty<AutomationLane>());
        var arranger = new StudioArranger(project);

        IReadOnlyList<StudioClipEvent> events = arranger.ClipEventsBetween(0, 100);

        Assert.Equal(2, events.Count);
        Assert.Equal(StudioClipEventKind.Start, events[0].Kind);
        Assert.Equal(8, events[0].TimeSeconds, Tol);
        Assert.Equal(2, events[0].Clip.DeckSlot);
        Assert.Equal(StudioClipEventKind.Stop, events[1].Kind);
        Assert.Equal(38, events[1].TimeSeconds, Tol); // 8 + 30
    }

    [Fact]
    public void ClipEventsBetween_WindowIsHalfOpen_AndFiltersOutside()
    {
        var project = new StudioProject("p", 120, new[]
        {
            Clip(0, start: 0, lengthSeconds: 10),   // Start at 0, Stop at 10
            Clip(1, start: 10, lengthSeconds: 5),   // Start at 10
        }, Array.Empty<AutomationLane>());
        var arranger = new StudioArranger(project);

        // [0,10): includes Start@0, excludes Stop@10 and Start@10 (half-open upper bound).
        IReadOnlyList<StudioClipEvent> first = arranger.ClipEventsBetween(0, 10);
        Assert.Single(first);
        Assert.Equal(0, first[0].TimeSeconds, Tol);

        // [10,20): clip 0 Stop@10, clip 1 Start@10, clip 1 Stop@15 all land.
        IReadOnlyList<StudioClipEvent> second = arranger.ClipEventsBetween(10, 20);
        Assert.Equal(3, second.Count);
        Assert.Equal(10, second[0].TimeSeconds, Tol); // the two @10 events sort first
        Assert.Equal(15, second[^1].TimeSeconds, Tol);
    }

    [Fact]
    public void ClipEventsBetween_OpenEndedClip_EmitsNoStop()
    {
        var project = new StudioProject("p", 120, new[] { Clip(0, start: 0, lengthSeconds: null) },
            Array.Empty<AutomationLane>());
        var arranger = new StudioArranger(project);

        IReadOnlyList<StudioClipEvent> events = arranger.ClipEventsBetween(0, 1000);

        Assert.Single(events);
        Assert.Equal(StudioClipEventKind.Start, events[0].Kind);
    }

    // --- automation actions ---

    private static StudioArranger WithLane(AutomationTarget target, int deck, params (double t, double v)[] keys)
        => new(new StudioProject("p", 120, Array.Empty<StudioClip>(), new[]
        {
            new AutomationLane(target, deck, keys.Select(k => new AutomationKeyframe(k.t, k.v)).ToList()),
        }));

    [Fact]
    public void AutomationActionsAt_DeckGain_EmitsAbsoluteChannelGain_StampedStudio()
    {
        StudioArranger arranger = WithLane(AutomationTarget.DeckGain, deck: 2, (0, 0.0), (10, 1.0));

        PerformanceAction action = Assert.Single(arranger.AutomationActionsAt(5));

        Assert.Equal(PerformanceActionKind.MixerChannelGain, action.Kind);
        Assert.Equal(ActionInputMode.Absolute, action.InputMode);
        Assert.Equal(2, action.Slot);
        Assert.Equal(0.5, action.Value, Tol);
        Assert.Equal(StudioArranger.Origin, action.Origin);
    }

    [Theory]
    [InlineData(AutomationTarget.EqLow, "Low")]
    [InlineData(AutomationTarget.EqMid, "Mid")]
    [InlineData(AutomationTarget.EqHigh, "High")]
    public void AutomationActionsAt_EqBands_CarryBandArgument(AutomationTarget target, string band)
    {
        StudioArranger arranger = WithLane(target, deck: 0, (0, 0.5));

        PerformanceAction action = Assert.Single(arranger.AutomationActionsAt(0));

        Assert.Equal(PerformanceActionKind.MixerEqBand, action.Kind);
        Assert.Equal(band, action.Argument);
    }

    [Theory]
    [InlineData(AutomationTarget.Filter, PerformanceActionKind.MixerFilter)]
    [InlineData(AutomationTarget.Pitch, PerformanceActionKind.DeckPitch)]
    public void AutomationActionsAt_FilterAndPitch_MapToTheirKinds(AutomationTarget target, PerformanceActionKind kind)
    {
        StudioArranger arranger = WithLane(target, deck: 1, (0, 0.5));
        Assert.Equal(kind, Assert.Single(arranger.AutomationActionsAt(0)).Kind);
    }

    [Fact]
    public void AutomationActionsAt_EmptyLane_IsSkipped()
    {
        var arranger = new StudioArranger(new StudioProject("p", 120, Array.Empty<StudioClip>(), new[]
        {
            new AutomationLane(AutomationTarget.DeckGain, 0, Array.Empty<AutomationKeyframe>()),
        }));

        Assert.Empty(arranger.AutomationActionsAt(0));
    }
}
