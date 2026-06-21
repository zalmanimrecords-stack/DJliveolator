namespace Liveolator.Core.Library.Import;

/// <summary>
/// One cue point parsed from another DJ app's library, in a source-agnostic form. Position is in
/// <em>seconds</em> from track start (every supported source exports seconds or milliseconds, never
/// Liveolator's samples); the planner converts to samples at import time.
/// </summary>
/// <param name="Index">
/// Target hot-cue slot (0-based). A value of <see cref="MemoryCue"/> (-1) marks an unindexed
/// memory/primary cue, which the planner routes to the track's primary cue.
/// </param>
/// <param name="PositionSeconds">Cue position in seconds from track start (non-negative).</param>
/// <param name="Label">Optional performer label (e.g. "Drop"); null when unlabelled.</param>
/// <param name="Color">Optional 0xRRGGBB display color; null when the source set none.</param>
public sealed record ImportedCue(int Index, double PositionSeconds, string? Label = null, int? Color = null)
{
    /// <summary>Sentinel <see cref="Index"/> for a memory/primary cue with no hot-cue slot.</summary>
    public const int MemoryCue = -1;

    /// <summary>True when this is an unindexed memory/primary cue rather than a numbered hot cue.</summary>
    public bool IsMemoryCue => Index == MemoryCue;
}
