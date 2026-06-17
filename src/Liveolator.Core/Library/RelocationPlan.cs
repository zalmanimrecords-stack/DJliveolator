namespace Liveolator.Core.Library;

/// <summary>One resolved relocation: the catalogued <paramref name="OldPath"/> that went missing,
/// paired with the <paramref name="NewFile"/> found under the new root that the catalog should
/// re-key it to (carrying the new path + fingerprint).</summary>
public readonly record struct RelocationMatch(string OldPath, ScannedFile NewFile);

/// <summary>
/// Result of <see cref="RelocationPlanner"/>: the missing entries that were matched to a candidate
/// under the new root (<see cref="Matches"/>) and those that could not be (<see cref="Unmatched"/>).
/// Purely a proposal — applying it (re-keying the catalog) is a separate, explicit step.
/// </summary>
public sealed record RelocationPlan(
    IReadOnlyList<RelocationMatch> Matches,
    IReadOnlyList<ScannedFile> Unmatched);
