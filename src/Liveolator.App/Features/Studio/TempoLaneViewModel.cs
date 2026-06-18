using System.Collections.Generic;
using System.Linq;
using Liveolator.Core.Studio;

namespace Liveolator.App.Features.Studio;

/// <summary>
/// The project tempo curve as an editable 0..1 lane: the curve editor edits 0..1 points, which map
/// linearly to the BPM range [<see cref="MinBpm"/>, <see cref="MaxBpm"/>] (so 0.5 ≈ mid-range).
/// <see cref="ToTempoCurve"/> / <see cref="Load"/> convert to/from the Core <see cref="TempoCurve"/>.
/// </summary>
public sealed class TempoLaneViewModel : EditableCurveBase
{
    public const double MinBpm = 60.0;
    public const double MaxBpm = 200.0;

    public static double ValueToBpm(double value) => MinBpm + (System.Math.Clamp(value, 0, 1) * (MaxBpm - MinBpm));

    public static double BpmToValue(double bpm) => System.Math.Clamp((bpm - MinBpm) / (MaxBpm - MinBpm), 0, 1);

    /// <summary>Project the curve to a Core <see cref="TempoCurve"/> (BPM keyframes, time-ordered).</summary>
    public TempoCurve ToTempoCurve() => new(
        Points.OrderBy(p => p.TimeSeconds)
            .Select(p => new TempoKeyframe(p.TimeSeconds, ValueToBpm(p.Value)))
            .ToList());

    /// <summary>Replace the lane's points from a saved <see cref="TempoCurve"/>.</summary>
    public void Load(TempoCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        Points.Clear();
        foreach (TempoKeyframe k in curve.Keyframes.OrderBy(k => k.TimeSeconds))
            Points.Add(new AutomationPointViewModel(k.TimeSeconds, BpmToValue(k.Bpm)));
    }
}
