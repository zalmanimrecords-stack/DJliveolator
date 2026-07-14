using Liveolator.Core.Mapping;
using Xunit;

namespace Liveolator.Core.Tests.Mapping;

/// <summary>
/// Soft-takeover (pickup) is a pure per-control policy: an absolute control does not move the
/// target until the incoming hardware value crosses (picks up) the current target value; once
/// picked up it tracks the hardware exactly (doc 27, Track D).
/// </summary>
public class SoftTakeoverTests
{
    [Fact]
    public void NotPickedUp_HoldsTargetValue()
    {
        var takeover = new SoftTakeover();

        // Target sits at 0.8; hardware is far below at 0.2 → no movement, target held.
        SoftTakeoverResult result = takeover.Evaluate(current: 0.8, incoming: 0.2);

        Assert.False(result.PickedUp);
        Assert.Equal(0.8, result.Value, precision: 9);
    }

    [Fact]
    public void RemainsHeld_WhileApproachingButNotYetCrossing()
    {
        var takeover = new SoftTakeover();

        // Hardware climbs toward the target from below but never reaches it.
        Assert.False(takeover.Evaluate(current: 0.8, incoming: 0.2).PickedUp);
        Assert.False(takeover.Evaluate(current: 0.8, incoming: 0.5).PickedUp);
        SoftTakeoverResult still = takeover.Evaluate(current: 0.8, incoming: 0.7);

        Assert.False(still.PickedUp);
        Assert.Equal(0.8, still.Value, precision: 9); // target untouched the whole approach
    }

    [Fact]
    public void PicksUp_WhenIncomingCrossesTargetFromBelow()
    {
        var takeover = new SoftTakeover();

        takeover.Evaluate(current: 0.8, incoming: 0.2); // arm below
        SoftTakeoverResult crossed = takeover.Evaluate(current: 0.8, incoming: 0.85);

        Assert.True(crossed.PickedUp);
        Assert.Equal(0.85, crossed.Value, precision: 9); // now tracks the hardware
    }

    [Fact]
    public void PicksUp_WhenIncomingCrossesTargetFromAbove()
    {
        var takeover = new SoftTakeover();

        takeover.Evaluate(current: 0.2, incoming: 0.9); // arm above
        SoftTakeoverResult crossed = takeover.Evaluate(current: 0.2, incoming: 0.1);

        Assert.True(crossed.PickedUp);
        Assert.Equal(0.1, crossed.Value, precision: 9);
    }

    [Fact]
    public void PicksUp_WhenFirstSampleLandsExactlyOnTarget()
    {
        var takeover = new SoftTakeover();

        // Hardware already matches the target → engage immediately, no jump.
        SoftTakeoverResult result = takeover.Evaluate(current: 0.5, incoming: 0.5);

        Assert.True(result.PickedUp);
        Assert.Equal(0.5, result.Value, precision: 9);
    }

    [Fact]
    public void OncePickedUp_TracksHardwareEvenAwayFromOldTarget()
    {
        var takeover = new SoftTakeover();

        takeover.Evaluate(current: 0.8, incoming: 0.2);
        takeover.Evaluate(current: 0.8, incoming: 0.85); // pick up here

        // Subsequent samples track directly, regardless of the (now moving) target.
        SoftTakeoverResult next = takeover.Evaluate(current: 0.85, incoming: 0.3);
        Assert.True(next.PickedUp);
        Assert.Equal(0.3, next.Value, precision: 9);
    }

    [Fact]
    public void Reset_RequiresPickupAgain()
    {
        var takeover = new SoftTakeover();

        takeover.Evaluate(current: 0.8, incoming: 0.2);
        takeover.Evaluate(current: 0.8, incoming: 0.85); // picked up
        takeover.Reset();

        // After reset the control must re-cross the target before it engages again.
        SoftTakeoverResult held = takeover.Evaluate(current: 0.85, incoming: 0.2);
        Assert.False(held.PickedUp);
        Assert.Equal(0.85, held.Value, precision: 9);
    }
}
