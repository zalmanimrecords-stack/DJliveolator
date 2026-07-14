namespace Liveolator.Core.Mapping;

/// <summary>
/// Outcome of a single <see cref="SoftTakeover.Evaluate"/> step: whether the control has picked up
/// the target yet, and the value to apply this step (the held target while not picked up, the
/// tracked hardware value once it has).
/// </summary>
/// <param name="PickedUp">True once the hardware has crossed (or met) the target and now tracks it.</param>
/// <param name="Value">The value the action should take this step, clamped to 0..1.</param>
public readonly record struct SoftTakeoverResult(bool PickedUp, double Value);
