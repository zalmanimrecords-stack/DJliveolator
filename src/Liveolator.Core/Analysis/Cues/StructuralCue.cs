namespace Liveolator.Core.Analysis.Cues;

/// <summary>
/// One detected structural point in a track: its musical <see cref="Kind"/>, its position in
/// seconds from track start (already phrase-quantized by the detector), and a 0..1 detection
/// confidence the placer uses to gate speculative cues. Pure data.
/// </summary>
/// <param name="Kind">The musical role of this point.</param>
/// <param name="PositionSeconds">Position in seconds from track start (non-negative).</param>
/// <param name="Confidence">Detection confidence in [0, 1].</param>
public readonly record struct StructuralCue(StructuralCueKind Kind, double PositionSeconds, double Confidence);
