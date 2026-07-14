using Liveolator.Core.Visuals.TrackPrograms;

namespace Liveolator.Core.Persistence;

/// <summary>
/// Persists authored track-to-visual timelines independently from the regenerable music and visual
/// catalogs. Implementations must use tolerant loads and atomic saves.
/// </summary>
public interface ITrackVisualProgramStore
{
    Task<TrackVisualProgram?> LoadAsync(
        string trackPath,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        TrackVisualProgram program,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string trackPath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrackVisualProgramSummary>> ListAsync(
        CancellationToken cancellationToken = default);
}
