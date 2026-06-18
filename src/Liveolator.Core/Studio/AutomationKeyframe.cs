namespace Liveolator.Core.Studio;

/// <summary>
/// One point on an automation curve: the control <see cref="Value"/> (0..1) at a timeline
/// position <see cref="TimeSeconds"/>. Values between keyframes are linearly interpolated
/// (see <see cref="AutomationLane.ValueAt"/>).
/// </summary>
public sealed record AutomationKeyframe(double TimeSeconds, double Value);
