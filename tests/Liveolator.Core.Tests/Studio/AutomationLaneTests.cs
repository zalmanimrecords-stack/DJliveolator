using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.Core.Tests.Studio;

public class AutomationLaneTests
{
    private const double Tol = 1e-9;

    private static AutomationLane Lane(params (double t, double v)[] keys)
        => new(AutomationTarget.DeckGain, DeckSlot: 1,
            keys.Select(k => new AutomationKeyframe(k.t, k.v)).ToList());

    [Fact]
    public void ValueAt_BetweenKeyframes_LinearlyInterpolates()
    {
        AutomationLane lane = Lane((0, 0.0), (10, 1.0));

        Assert.Equal(0.5, lane.ValueAt(5), Tol);
        Assert.Equal(0.25, lane.ValueAt(2.5), Tol);
    }

    [Fact]
    public void ValueAt_BeforeFirst_HoldsFirstValue()
    {
        AutomationLane lane = Lane((4, 0.3), (8, 0.9));
        Assert.Equal(0.3, lane.ValueAt(0), Tol);
        Assert.Equal(0.3, lane.ValueAt(4), Tol);
    }

    [Fact]
    public void ValueAt_AfterLast_HoldsLastValue()
    {
        AutomationLane lane = Lane((4, 0.3), (8, 0.9));
        Assert.Equal(0.9, lane.ValueAt(100), Tol);
        Assert.Equal(0.9, lane.ValueAt(8), Tol);
    }

    [Fact]
    public void ValueAt_SingleKeyframe_IsConstant()
    {
        AutomationLane lane = Lane((5, 0.42));
        Assert.Equal(0.42, lane.ValueAt(0), Tol);
        Assert.Equal(0.42, lane.ValueAt(5), Tol);
        Assert.Equal(0.42, lane.ValueAt(50), Tol);
    }

    [Fact]
    public void ValueAt_CoincidentKeyframes_TakesLaterValue()
    {
        AutomationLane lane = Lane((0, 0.0), (5, 0.2), (5, 0.8), (10, 1.0));
        Assert.Equal(0.8, lane.ValueAt(5), Tol);
    }

    [Fact]
    public void ValueAt_NoKeyframes_Throws()
    {
        var lane = new AutomationLane(AutomationTarget.Filter, 0, Array.Empty<AutomationKeyframe>());
        Assert.Throws<InvalidOperationException>(() => lane.ValueAt(0));
    }
}
