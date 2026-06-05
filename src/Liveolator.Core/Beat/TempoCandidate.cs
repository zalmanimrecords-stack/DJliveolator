namespace Liveolator.Core.Beat;

/// <summary>
/// One tempo hypothesis with its relative strength. BPM detection is inherently ambiguous, so the
/// full candidate list (including half/double-time) is always exposed rather than hidden, letting
/// the performer pick when detection is unsure (doc 03).
/// </summary>
/// <param name="Bpm">The candidate tempo.</param>
/// <param name="Strength">Relative strength, higher is stronger.</param>
public sealed record TempoCandidate(double Bpm, double Strength);
