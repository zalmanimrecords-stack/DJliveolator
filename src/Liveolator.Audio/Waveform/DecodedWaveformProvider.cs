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
    /// <summary>Default decode rate for the overview. 16 kHz puts Nyquist at 8 kHz so the high
    /// (hats/air) band of the 3-band strip carries real signal — at 8 kHz nothing above 4 kHz survives
    /// the decode and the high layer would be fiction. Still cheap: the transient mono buffer for a
    /// 6-minute track is ~22 MB of floats, freed as soon as the overview is built.</summary>
    public const int DefaultOverviewSampleRate = 16_000;

    /// <summary>
    /// Target overview resolution in buckets PER SECOND. A fixed total bucket count makes long tracks
    /// coarse (a 6-min track at 6000 buckets ≈ 17 buckets/s ≈ 60 ms/bucket — the kick attack quantizes and
    /// smears when zoomed in). A fixed density (≈ one bucket per 6–7 ms) keeps each kick its own crisp
    /// column at performance zoom regardless of track length. The caller's requested count is treated as a
    /// floor, so short tracks still get a smooth strip.
    /// </summary>
    public const int TargetBucketsPerSecond = 150;

    /// <summary>Hard ceiling on the bucket count so a pathologically long/corrupt file can't allocate unbounded.</summary>
    public const int MaxBuckets = 250_000;

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

            // Resolution by DENSITY, not a fixed count: derive buckets from the decoded duration so each
            // kick stays a crisp column at zoom (the caller's count is only a floor). Capped so a huge file
            // can't allocate without bound.
            int densityBuckets = (int)Math.Min(
                MaxBuckets, (long)Math.Ceiling((double)samples.Count / _overviewSampleRate * TargetBucketsPerSecond));
            int effectiveBuckets = Math.Clamp(Math.Max(bucketCount, densityBuckets), 1, MaxBuckets);

            // Pass the overview rate so WaveformBuilder also derives the low-frequency (kick) band, which
            // the deck strip draws as a distinct overlay for beat alignment (sync by eye).
            WaveformOverview overview = WaveformBuilder.Build(
                CollectionsMarshal.AsSpan(samples), effectiveBuckets, _overviewSampleRate);
            // Duration from the mono sample count at the (known) overview rate, so the deck can place a
            // beat-grid overlay without a second decode. Empty overviews stay Empty (no duration).
            return overview.IsEmpty
                ? overview
                : overview with { DurationSeconds = (double)samples.Count / _overviewSampleRate };
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
