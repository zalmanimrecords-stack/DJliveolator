namespace Liveolator.Core.Visuals.TrackPrograms;

/// <summary>Lightweight authored-program metadata for library status and management views.</summary>
public sealed record TrackVisualProgramSummary(
    string ProgramId,
    string TrackPath,
    int CueCount,
    TrackVisualFallback Fallback);
