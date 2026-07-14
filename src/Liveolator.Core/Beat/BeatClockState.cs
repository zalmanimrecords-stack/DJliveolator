namespace Liveolator.Core.Beat;

/// <summary>
/// An immutable snapshot of the beat clock. Published by swapping the whole record so there is no
/// shared mutable state across threads (doc 00/03).
/// </summary>
/// <param name="Bpm">Current tempo.</param>
/// <param name="Confidence">Detection confidence, 0..1.</param>
/// <param name="BeatPhase">Position within the current beat, 0..1.</param>
/// <param name="BarPhase">Position within the current bar, 0..1.</param>
/// <param name="BeatCount">Monotonic beat count since the last grid reset.</param>
/// <param name="BarNumber">Current bar number.</param>
/// <param name="IsBeat">True on the frame a beat boundary is crossed.</param>
/// <param name="IsDownbeat">True on the frame a bar boundary is crossed.</param>
/// <param name="IsLocked">True when tempo is frozen against jitter.</param>
/// <param name="Source">Where tempo/phase comes from.</param>
/// <param name="Candidates">All current tempo hypotheses.</param>
public sealed record BeatClockState(
    double Bpm,
    double Confidence,
    double BeatPhase,
    double BarPhase,
    int BeatCount,
    int BarNumber,
    bool IsBeat,
    bool IsDownbeat,
    bool IsLocked,
    BeatClockSource Source,
    IReadOnlyList<TempoCandidate> Candidates)
{
    /// <summary>A silent, unlocked starting state with no tempo information.</summary>
    public static BeatClockState Idle { get; } = new(
        Bpm: 0,
        Confidence: 0,
        BeatPhase: 0,
        BarPhase: 0,
        BeatCount: 0,
        BarNumber: 0,
        IsBeat: false,
        IsDownbeat: false,
        IsLocked: false,
        Source: BeatClockSource.Manual,
        Candidates: Array.Empty<TempoCandidate>());
}
