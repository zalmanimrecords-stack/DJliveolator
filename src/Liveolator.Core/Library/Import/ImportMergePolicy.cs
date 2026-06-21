namespace Liveolator.Core.Library.Import;

/// <summary>
/// How an import treats tracks that already exist in Liveolator's catalog/cue store.
/// </summary>
public enum ImportMergePolicy
{
    /// <summary>
    /// Default, non-destructive: a brand-new track is added in full; an existing track gains only the
    /// analysis fields Liveolator is <em>missing</em> (e.g. fills a null BPM/key), and its cues are
    /// imported only if it has no stored cues yet. Never overwrites the DJ's existing Liveolator work
    /// (global standard #7).
    /// </summary>
    FillGaps,

    /// <summary>
    /// The source wins: imported BPM/key/metadata replace the catalog's, and imported cues replace any
    /// stored cues. Use to trust a hand-curated source library over Liveolator's auto-analysis.
    /// </summary>
    Overwrite,
}
