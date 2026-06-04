using Liveolator.Core.Analysis;

namespace Liveolator.Core.Library.Music;

/// <summary>
/// Decides whether an analyzed track is fully Ok or only PartiallyAnalyzed (low confidence),
/// powering the UI's "Low confidence" filter and status dot (doc 16 / libraries mockup).
/// </summary>
public static class TrackStatusPolicy
{
    /// <summary>Minimum tempo-detection confidence to be considered fully analyzed.</summary>
    public const double MinBpmConfidence = 0.10;

    /// <summary>Minimum key-detection (chroma correlation) confidence to be considered fully analyzed.</summary>
    public const double MinKeyConfidence = 0.55;

    public static MediaAnalysisStatus For(TrackAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        bool confident = result.Bpm.Confidence >= MinBpmConfidence
                         && result.Key.Confidence >= MinKeyConfidence;
        return confident ? MediaAnalysisStatus.Ok : MediaAnalysisStatus.PartiallyAnalyzed;
    }
}
