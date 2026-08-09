namespace Liveolator.Core.Studio.Set;

/// <summary>
/// A track that was available but did not make the set, and why. <paramref name="NeededWarpPercent"/> is
/// filled for <see cref="RejectReason.OutsideTempoRange"/> — it turns "the set came out short" into a
/// specific next move ("six tracks missed the limit by under one percent").
/// </summary>
public sealed record RejectedCandidate(
    string Path,
    string Title,
    RejectReason Reason,
    double? NeededWarpPercent = null);
