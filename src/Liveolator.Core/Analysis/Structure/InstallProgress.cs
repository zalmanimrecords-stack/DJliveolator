namespace Liveolator.Core.Analysis.Structure;

/// <summary>The stage an <see cref="IAdvancedAnalysisInstaller"/> install has reached.</summary>
public enum InstallPhase
{
    Downloading,
    Verifying,
    Extracting,
    InstallingPackages,
    Done,
    Failed,
}

/// <summary>
/// Progress report for an advanced-analysis runtime install (doc 32). Pure data, surfaced to a UI
/// progress bar later (no UI wiring here).
/// </summary>
/// <param name="Phase">Current install stage.</param>
/// <param name="Fraction">Overall completion in <c>[0, 1]</c>.</param>
/// <param name="Message">Human-readable status line for the current phase.</param>
public sealed record InstallProgress(InstallPhase Phase, double Fraction, string Message);
