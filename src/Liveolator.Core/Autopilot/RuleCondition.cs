namespace Liveolator.Core.Autopilot;

/// <summary>
/// An optional gate on a rule: confidence floor, energy window, and track-position window. Each is
/// nullable so unset checks pass. Thresholds are data (not hardcoded), since they are
/// genre-dependent (doc 10 risk).
/// </summary>
public sealed record RuleCondition(
    double? MinConfidence = null,
    double? MinEnergy = null,
    double? MaxEnergy = null,
    double? TrackPositionFrom = null,
    double? TrackPositionTo = null)
{
    /// <summary>A condition that always passes.</summary>
    public static RuleCondition None { get; } = new();

    /// <summary>True when all set thresholds are satisfied by the given tick inputs.</summary>
    public bool IsMet(double confidence, double energy, double trackPosition)
    {
        if (MinConfidence is double minConfidence && confidence < minConfidence)
            return false;
        if (MinEnergy is double minEnergy && energy < minEnergy)
            return false;
        if (MaxEnergy is double maxEnergy && energy > maxEnergy)
            return false;
        if (TrackPositionFrom is double from && trackPosition < from)
            return false;
        if (TrackPositionTo is double to && trackPosition > to)
            return false;
        return true;
    }
}
