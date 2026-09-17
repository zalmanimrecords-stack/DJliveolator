using Liveolator.Core.Library.Import;

namespace Liveolator.Mcp.Contracts;

/// <summary>One track where the scanning server's tempo disagrees with the local grid.</summary>
public sealed record ServerPullDisagreementDto(string Path, string Title, double LocalBpm, double ServerBpm)
{
    public static ServerPullDisagreementDto From(ServerCatalogDisagreement d)
        => new(d.Path, d.Title, d.LocalBpm, d.ServerBpm);
}

/// <summary>
/// Agent-facing result of pulling analysis from a scanning server's catalog. <see cref="Applied"/>
/// distinguishes a preview from a write, so an agent can always tell whether the numbers describe what
/// WOULD happen or what did — the same shape answers both.
/// </summary>
public sealed record ServerPullSummaryDto(
    bool Applied,
    int TracksGainingAnalysis,
    int TracksNowPhaseSyncReady,
    int NotFoundLocally,
    int HandCorrectedUntouched,
    int SkippedInUse,
    IReadOnlyList<ServerPullDisagreementDto> Disagreements,
    string Message)
{
    public static ServerPullSummaryDto From(ServerCatalogPullPlan plan, bool applied) => new(
        applied,
        plan.TracksGainingAnalysis,
        plan.TracksNowPhaseSyncReady,
        plan.NotFoundLocally,
        plan.HandCorrectedUntouched,
        plan.SkippedInUse,
        plan.Disagreements.Select(ServerPullDisagreementDto.From).ToList(),
        plan.Describe());
}
