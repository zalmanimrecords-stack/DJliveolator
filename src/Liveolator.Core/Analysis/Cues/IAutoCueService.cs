using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Liveolator.Core.Analysis.Cues;

/// <summary>
/// Runs automatic hot-cue placement over a batch of tracks and persists the results (doc 11/16). The
/// seam the UI depends on so a library "Auto-cue track(s)" action — or a background pass — can request
/// placement without binding to the concrete decode/analysis implementation (Core iron rule #3).
/// </summary>
public interface IAutoCueService
{
    /// <summary>
    /// Places and persists auto cues for every track in <paramref name="trackPaths"/>. Tracks that cannot
    /// be decoded or whose structure is undetectable are skipped without stopping the pass. Honours
    /// cancellation between tracks.
    /// </summary>
    Task<AutoCueOutcome> RunAsync(
        IReadOnlyList<string> trackPaths,
        IProgress<AutoCueProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
