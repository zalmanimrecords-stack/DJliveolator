using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.Core.Waveform;

namespace Liveolator.App.Tests.Live;

/// <summary>
/// A test double for <see cref="IWaveformProvider"/>: returns a preset overview (peaks + duration) so the
/// deck view-model's waveform/beat-grid derivation can be exercised without any native decode.
/// </summary>
public sealed class FakeWaveformProvider : IWaveformProvider
{
    private readonly WaveformOverview _overview;

    public FakeWaveformProvider(WaveformOverview overview) => _overview = overview;

    public static FakeWaveformProvider WithDuration(double durationSeconds, int peakCount = 8)
        => new(new WaveformOverview(new float[peakCount], durationSeconds));

    /// <summary>An overview carrying all three frequency bands, like a real decode produces.</summary>
    public static FakeWaveformProvider WithBands(double durationSeconds, int peakCount = 8)
        => new(new WaveformOverview(
            new float[peakCount], durationSeconds,
            LowPeaks: new float[peakCount], MidPeaks: new float[peakCount], HighPeaks: new float[peakCount]));

    public Task<WaveformOverview> GetOverviewAsync(
        string filePath, int bucketCount, CancellationToken cancellationToken = default)
        => Task.FromResult(_overview);
}
