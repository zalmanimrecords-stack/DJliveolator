using System.Collections.Generic;
using System.Linq;
using Liveolator.Core.Analysis.Structure;

namespace Liveolator.Core.Analysis.Cues;

/// <summary>
/// Adapts a real <see cref="SongStructure"/> (from the offline Python/librosa analyzer, doc 32) into
/// the existing <see cref="StructuralCueResult"/> shape so the unchanged <see cref="AutoCuePlacer"/>
/// can anchor hot cues on real section boundaries. When no structure is available the caller falls
/// back to the heuristic <see cref="StructuralCueDetector"/> — this adapter never rewrites that logic.
/// </summary>
public static class SongStructureCues
{
    // ML-detected boundaries are trusted, so they clear AutoCuePlacer's default speculative floor (0.5).
    private const double SectionConfidence = 0.9;

    private static readonly IReadOnlyDictionary<string, StructuralCueKind> LabelToKind =
        new Dictionary<string, StructuralCueKind>(System.StringComparer.OrdinalIgnoreCase)
        {
            [SongSectionLabel.Intro] = StructuralCueKind.TrackStart,
            [SongSectionLabel.BuildUp] = StructuralCueKind.BuildUp,
            [SongSectionLabel.Drop] = StructuralCueKind.Drop,
            [SongSectionLabel.Breakdown] = StructuralCueKind.Breakdown,
            [SongSectionLabel.Outro] = StructuralCueKind.OutroStart,
            [SongSectionLabel.Section] = StructuralCueKind.Phrase,
        };

    /// <summary>
    /// Converts a detected structure into structural cues, or <c>null</c> when there is no structure to
    /// place (null input or no sections). Unknown labels map to a generic <see cref="StructuralCueKind.Phrase"/>.
    /// </summary>
    public static StructuralCueResult? ToStructuralCues(SongStructure? structure)
    {
        if (structure is null || structure.Sections.Count == 0)
            return null;

        var cues = structure.Ordered
            .Select(s => new StructuralCue(
                LabelToKind.TryGetValue(s.Label, out StructuralCueKind kind) ? kind : StructuralCueKind.Phrase,
                s.StartSeconds < 0.0 ? 0.0 : s.StartSeconds,
                SectionConfidence))
            .ToList();

        return new StructuralCueResult(cues, SectionConfidence);
    }
}
