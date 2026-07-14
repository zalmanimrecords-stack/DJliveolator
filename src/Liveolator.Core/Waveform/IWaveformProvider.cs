namespace Liveolator.Core.Waveform;

/// <summary>
/// Produces a track's <see cref="WaveformOverview"/> for the deck display (doc 11). The seam lives in
/// Core so the deck/UI depend only on the abstraction; the concrete implementation decodes the file in
/// the audio binding (<c>Liveolator.Audio</c>). A non-decodable or failing track yields
/// <see cref="WaveformOverview.Empty"/> rather than throwing, so the deck degrades to its placeholder.
/// </summary>
public interface IWaveformProvider
{
    /// <summary>
    /// Decode <paramref name="filePath"/> and reduce it to <paramref name="bucketCount"/> peaks.
    /// </summary>
    Task<WaveformOverview> GetOverviewAsync(
        string filePath, int bucketCount, CancellationToken cancellationToken = default);
}
