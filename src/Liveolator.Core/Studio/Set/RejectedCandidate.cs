namespace Liveolator.Core.Studio.Set;

/// <summary>
/// A track that was available but did not make the set, and why. <paramref name="NeededWarpPercent"/> is
/// filled for <see cref="RejectReason.OutsideTempoRange"/> and
/// <see cref="RejectReason.SeedOutsideTempoRange"/> — it turns "the set came out short" into a specific
/// next move ("six tracks missed the limit by under one percent").
/// <para><see cref="RejectReason.LengthCapReached"/> is the one entry that names no track: it is the
/// explicit non-rejection line for a cap that was honoured, with an empty <paramref name="Path"/> and the
/// untried count carried in <paramref name="Title"/> — the only field of this record that reaches an MCP
/// caller as free text.</para>
/// <para>Two entries are about why the set is short rather than about a track being absent:
/// <see cref="RejectReason.LengthCapReached"/> above, and <see cref="RejectReason.NoMixOutRunway"/>, which
/// names the record the chain stopped on — that one IS on the timeline, as its closing clip.</para>
/// </summary>
public sealed record RejectedCandidate(
    string Path,
    string Title,
    RejectReason Reason,
    double? NeededWarpPercent = null);
