namespace Liveolator.Core.Studio;

/// <summary>One point on the project tempo curve: the tempo (<see cref="Bpm"/>) at a timeline
/// position (<see cref="TimeSeconds"/>). Values between keyframes are linearly interpolated.</summary>
public sealed record TempoKeyframe(double TimeSeconds, double Bpm);
