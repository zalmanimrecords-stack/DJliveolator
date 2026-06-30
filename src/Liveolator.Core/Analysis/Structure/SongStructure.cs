using System.Collections.Generic;
using System.Linq;

namespace Liveolator.Core.Analysis.Structure;

/// <summary>
/// A track's detected musical structure: an ordered list of <see cref="SongSection"/> boundaries
/// plus the analyzer that produced it (doc 32). Result of <see cref="ISongStructureAnalyzer"/>.
/// Pure data — produced offline, cached to the catalog, never touched on the realtime path.
/// </summary>
/// <param name="Sections">Section boundaries in ascending start-time order.</param>
/// <param name="AnalyzedWith">Free-text provenance, e.g. <c>"librosa 0.10.2"</c>.</param>
public sealed record SongStructure(IReadOnlyList<SongSection> Sections, string AnalyzedWith)
{
    /// <summary>Sections sorted by start time (the analyzer normalizes; this guards callers).</summary>
    public IReadOnlyList<SongSection> Ordered => Sections.OrderBy(s => s.StartSeconds).ToList();
}
