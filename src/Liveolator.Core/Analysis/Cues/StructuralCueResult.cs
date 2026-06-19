using System.Collections.Generic;

namespace Liveolator.Core.Analysis.Cues;

/// <summary>
/// The output of <see cref="StructuralCueDetector"/>: the structural points found in a track plus an
/// <see cref="OverallConfidence"/> that summarises how trustworthy the analysis is (driven mainly by
/// tempo confidence and how much the energy contour actually varies). The <see cref="AutoCuePlacer"/>
/// turns this into a <see cref="TrackCueSet"/>. Pure data.
/// </summary>
/// <param name="Cues">The detected structural points, in track order.</param>
/// <param name="OverallConfidence">A 0..1 summary confidence for the whole analysis.</param>
public sealed record StructuralCueResult(IReadOnlyList<StructuralCue> Cues, double OverallConfidence)
{
    /// <summary>An empty result — no structure detected, zero confidence.</summary>
    public static StructuralCueResult Empty { get; } = new(System.Array.Empty<StructuralCue>(), 0.0);
}
