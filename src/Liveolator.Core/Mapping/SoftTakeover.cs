namespace Liveolator.Core.Mapping;

/// <summary>
/// Soft-takeover (pickup) policy for a single absolute control: the target value does not jump when
/// the hardware moves out of sync with it. Until the incoming hardware value crosses (or exactly
/// meets) the current target, the target is held; once it picks up, the control tracks the hardware
/// directly. Pure, per-control state so it is unit-tested without any device (doc 27, Track D).
/// </summary>
/// <remarks>
/// One instance tracks one physical control. The caller holds an instance per soft-takeover binding
/// and feeds each absolute sample through <see cref="Evaluate"/>.
/// </remarks>
public sealed class SoftTakeover
{
    private bool _pickedUp;
    private double? _lastIncoming;

    /// <summary>True once the control has picked up the target and is tracking the hardware.</summary>
    public bool IsPickedUp => _pickedUp;

    /// <summary>
    /// Evaluates one incoming absolute sample against the current target.
    /// </summary>
    /// <param name="current">The target's current value (0..1) the control is meant to control.</param>
    /// <param name="incoming">The newly-received absolute hardware value (0..1).</param>
    /// <returns>
    /// While not picked up: <c>(false, current)</c> — the target is held and does not jump. Once the
    /// hardware crosses or meets the target: <c>(true, incoming)</c> — and every subsequent sample
    /// tracks the hardware directly until <see cref="Reset"/> is called.
    /// </returns>
    public SoftTakeoverResult Evaluate(double current, double incoming)
    {
        double clampedCurrent = Math.Clamp(current, 0.0, 1.0);
        double clampedIncoming = Math.Clamp(incoming, 0.0, 1.0);

        if (_pickedUp)
            return new SoftTakeoverResult(true, clampedIncoming);

        if (HasCrossed(clampedIncoming, clampedCurrent))
        {
            _pickedUp = true;
            _lastIncoming = clampedIncoming;
            return new SoftTakeoverResult(true, clampedIncoming);
        }

        _lastIncoming = clampedIncoming;
        return new SoftTakeoverResult(false, clampedCurrent);
    }

    /// <summary>
    /// Re-arms the policy so the control must pick up the target again before it tracks. Call when
    /// the binding's target is reassigned (e.g. a deck reload or a mode switch) so a stale hardware
    /// position cannot jump the new target.
    /// </summary>
    public void Reset()
    {
        _pickedUp = false;
        _lastIncoming = null;
    }

    /// <summary>
    /// True when the incoming value has reached the target: it exactly meets it, or it moved from one
    /// side of the target to the other (or onto it) since the last sample.
    /// </summary>
    private bool HasCrossed(double incoming, double target)
    {
        if (incoming == target)
            return true;

        if (_lastIncoming is not double previous)
            return false; // first sample on one side: arm, don't engage.

        // Crossed if the previous sample and this one straddle the target (signs differ or one is 0).
        return (previous - target) * (incoming - target) <= 0.0;
    }
}
