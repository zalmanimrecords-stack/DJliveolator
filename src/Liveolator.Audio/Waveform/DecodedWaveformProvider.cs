using System.Runtime.InteropServices;
using Liveolator.Core.Analysis;
using Liveolator.Core.Waveform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Audio.Waveform;

/// <summary>
/// <see cref="IWaveformProvider"/> over the offline <see cref="IAudioDecoder"/>: decodes the track to
/// mono at a low "overview" sample rate (full fidelity is wasted on a display strip), accumulates it,
/// and reduces it to peaks with the pure Core <see cref="WaveformBuilder"/>. Decoding at the reduced
/// rate keeps the transient buffer small and is freed as soon as the overview is built.
/// </summary>
/// <remarks>
/// Failures degrade, never throw: an undecodable file, a decode error, or an empty track returns
/// <see cref="WaveformOverview.Empty"/> with a warning, so the deck falls back to its placeholder
/// (global standards #16/#26). Cancellation propagates.
/// </remarks>
public sealed class DecodedWaveformProvider : IWaveformProvider
{
    /// <summary>Default decode rate for the overview — enough to catch transients, cheap to hold.</summary>
    public const int DefaultOverviewSampleRate = 8_000;

    private readonly IAudioDecoder _decoder;
    private readonly int _overviewSampleRate;
    private readonly ILogger<DecodedWaveformProvider> _logger;

    public DecodedWaveformProvider(
        IAudioDecoder decoder,
        int overviewSampleRate = DefaultOverviewSampleRate,
        ILogger<DecodedWaveformProvider>? logger = null)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        if (overviewSampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(overviewSampleRate));
        _overviewSampleRate = overviewSampleRate;
        _logger = logger ?? NullLogger<DecodedWaveformProvider>.Instance;
    }

    public async Task<WaveformOverview> GetOverviewAsync(
        string filePath, int bucketCount, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("filePath must be a non-empty path.", nameof(filePath));
        if (bucketCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(bucketCount), bucketCount, "Bucket count must be positive.");

        if (!_decoder.CanDecode(filePath))
        {
            _logger.LogWarning("No decoder for {Path}; waveform unavailable.", filePath);
            return WaveformOverview.Empty;
        }

        try
        {
            var samples = new List<float>();
            await foreach (ReadOnlyMemory<float> block in
                _decoder.DecodeMonoAsync(filePath, _overviewSampleRate, cancellationToken).ConfigureAwait(false))
            {
                Append(samples, block);
            }

            return WaveformBuilder.Build(CollectionsMarshal.AsSpan(samples), bucketCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Decoding {Path} for waveform failed; returning empty.", filePath);
            return WaveformOverview.Empty;
        }
    }

    // Copies a decoded block into the accumulator. Kept synchronous so the ReadOnlySpan never lives
    // across an await (ref structs are not allowed in async bodies).
    private static void Append(List<float> destination, ReadOnlyMemory<float> block)
    {
        ReadOnlySpan<float> span = block.Span;
        for (int i = 0; i < span.Length; i++)
            destination.Add(span[i]);
    }
}
