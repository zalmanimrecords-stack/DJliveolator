using System.Security.Cryptography;
using System.Text;
using Liveolator.Core.Waveform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Audio.Waveform;

/// <summary>Shares a full-resolution overview between library, decks and studio, and across launches.
/// File length + modification time invalidate edited tracks. Per-file gates avoid duplicate NAS reads;
/// cancelling a waiter never cancels the current reader. Failed/cancelled results are never cached.</summary>
public sealed class CachedWaveformProvider : IWaveformProvider
{
    private const int MinimumBuckets = 6_000;
    private readonly IWaveformProvider _inner;
    private readonly WaveformDiskCache _disk;
    private readonly object _gateLock = new();
    private readonly Dictionary<string, LoadGate> _gates = new();

    public CachedWaveformProvider(IWaveformProvider inner, string cacheDirectory,
        ILogger<CachedWaveformProvider>? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        _disk = new WaveformDiskCache(cacheDirectory, logger ?? NullLogger<CachedWaveformProvider>.Instance);
    }

    public async Task<WaveformOverview> GetOverviewAsync(
        string filePath, int bucketCount, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (bucketCount <= 0) throw new ArgumentOutOfRangeException(nameof(bucketCount));
        cancellationToken.ThrowIfCancellationRequested();
        int resolution = Math.Clamp(Math.Max(MinimumBuckets, bucketCount), 1, DecodedWaveformProvider.MaxBuckets);
        string? key = CacheKey(filePath, resolution);
        if (key is null)
            return await _inner.GetOverviewAsync(filePath, resolution, cancellationToken).ConfigureAwait(false);

        LoadGate gate;
        lock (_gateLock)
        {
            if (!_gates.TryGetValue(key, out gate!)) _gates[key] = gate = new LoadGate();
            gate.Users++;
        }
        bool entered = false;
        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            cancellationToken.ThrowIfCancellationRequested();
            WaveformOverview? cached = _disk.Read(key);
            if (cached is not null) return cached;

            WaveformOverview overview = await _inner.GetOverviewAsync(filePath, resolution, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // An edit while decoding must not be stored under the old file revision.
            if (!overview.IsEmpty && key == CacheKey(filePath, resolution)) _disk.Write(key, overview);
            return overview;
        }
        finally
        {
            if (entered) gate.Semaphore.Release();
            lock (_gateLock)
            {
                if (--gate.Users == 0)
                {
                    _gates.Remove(key);
                    gate.Semaphore.Dispose();
                }
            }
        }
    }

    private static string? CacheKey(string path, int resolution)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return null;
            string canonical = OperatingSystem.IsWindows() ? file.FullName.ToUpperInvariant() : file.FullName;
            // Bump the format key whenever the overview rate, filter design or density policy changes.
            string identity = $"wave-v1|{canonical}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|{resolution}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null; // Preserve the decoder's normal failure/virtual-file behavior.
        }
    }

    private sealed class LoadGate
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Users;
    }
}
