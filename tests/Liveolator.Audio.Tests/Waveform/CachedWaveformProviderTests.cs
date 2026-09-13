using Liveolator.Audio.Waveform;
using Liveolator.Core.Waveform;

namespace Liveolator.Audio.Tests.Waveform;

public sealed class CachedWaveformProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "liveolator-wave-cache-" + Guid.NewGuid().ToString("N"));
    private string Track => Path.Combine(_root, "track.flac");
    private string Cache => Path.Combine(_root, "cache");

    public CachedWaveformProviderTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Track, "audio revision one");
    }

    [Fact]
    public async Task Library_then_deck_reuses_the_same_high_resolution_decode()
    {
        var inner = new CountingProvider();
        var provider = new CachedWaveformProvider(inner, Cache);
        var library = await provider.GetOverviewAsync(Track, 2000);
        var deck = await provider.GetOverviewAsync(Track, 6000);
        Assert.Equal(1, inner.Calls);
        Assert.True(inner.LastBuckets >= 6000);
        Assert.Equal(library.Peaks, deck.Peaks);
    }

    [Fact]
    public async Task New_provider_reads_persisted_bands_without_decoding_again()
    {
        var inner = new CountingProvider();
        var first = await new CachedWaveformProvider(inner, Cache).GetOverviewAsync(Track, 6000);
        var second = await new CachedWaveformProvider(inner, Cache).GetOverviewAsync(Track, 6000);
        Assert.Equal(1, inner.Calls);
        Assert.Equal(first.DurationSeconds, second.DurationSeconds);
        Assert.Equal(first.Peaks, second.Peaks);
        Assert.Equal(first.LowPeaks, second.LowPeaks);
        Assert.Equal(first.MidPeaks, second.MidPeaks);
        Assert.Equal(first.HighPeaks, second.HighPeaks);
    }

    [Fact]
    public async Task Changed_audio_file_invalidates_the_cache()
    {
        var inner = new CountingProvider();
        var provider = new CachedWaveformProvider(inner, Cache);
        await provider.GetOverviewAsync(Track, 6000);
        File.AppendAllText(Track, "changed");
        await provider.GetOverviewAsync(Track, 6000);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Concurrent_library_and_deck_requests_decode_once()
    {
        var inner = new CountingProvider { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var provider = new CachedWaveformProvider(inner, Cache);
        var first = provider.GetOverviewAsync(Track, 2000);
        await inner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = provider.GetOverviewAsync(Track, 6000);
        inner.Hold.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Cancelled_waiter_does_not_cancel_another_request()
    {
        var inner = new CountingProvider { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var provider = new CachedWaveformProvider(inner, Cache);
        var first = provider.GetOverviewAsync(Track, 6000);
        await inner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();
        var second = provider.GetOverviewAsync(Track, 6000, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        inner.Hold.SetResult();
        Assert.False((await first).IsEmpty);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Empty_result_is_not_cached_and_can_be_retried()
    {
        var inner = new CountingProvider { Empty = true };
        var provider = new CachedWaveformProvider(inner, Cache);
        Assert.True((await provider.GetOverviewAsync(Track, 6000)).IsEmpty);
        inner.Empty = false;
        Assert.False((await provider.GetOverviewAsync(Track, 6000)).IsEmpty);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Corrupt_disk_entry_is_rebuilt()
    {
        var inner = new CountingProvider();
        await new CachedWaveformProvider(inner, Cache).GetOverviewAsync(Track, 6000);
        var path = Assert.Single(Directory.GetFiles(Cache, "*.wave"));
        File.WriteAllText(path, "broken");
        Assert.False((await new CachedWaveformProvider(inner, Cache).GetOverviewAsync(Track, 6000)).IsEmpty);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Cancelled_reader_releases_the_gate_for_a_waiting_deck()
    {
        var inner = new CountingProvider { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var provider = new CachedWaveformProvider(inner, Cache);
        using var cts = new CancellationTokenSource();
        var first = provider.GetOverviewAsync(Track, 2000, cts.Token);
        await inner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = provider.GetOverviewAsync(Track, 6000);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        inner.Hold.SetResult();
        Assert.False((await second.WaitAsync(TimeSpan.FromSeconds(5))).IsEmpty);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Unwritable_cache_does_not_hide_a_successfully_decoded_waveform()
    {
        File.WriteAllText(Cache, "a file blocks directory creation");
        var inner = new CountingProvider();
        Assert.False((await new CachedWaveformProvider(inner, Cache).GetOverviewAsync(Track, 6000)).IsEmpty);
    }

    [Fact]
    public async Task Invalid_binary_band_length_is_rebuilt()
    {
        var inner = new CountingProvider();
        await new CachedWaveformProvider(inner, Cache).GetOverviewAsync(Track, 6000);
        var path = Assert.Single(Directory.GetFiles(Cache, "*.wave"));
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(0x4C575631);
            writer.Write(120.0);
            writer.Write(-1);
        }
        Assert.False((await new CachedWaveformProvider(inner, Cache).GetOverviewAsync(Track, 6000)).IsEmpty);
        Assert.Equal(2, inner.Calls);
    }
    public void Dispose() => Directory.Delete(_root, true);

    private sealed class CountingProvider : IWaveformProvider
    {
        public int Calls;
        public int LastBuckets;
        public bool Empty;
        public TaskCompletionSource? Hold;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<WaveformOverview> GetOverviewAsync(string filePath, int bucketCount, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            LastBuckets = bucketCount;
            Started.TrySetResult();
            if (Hold is not null) await Hold.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return Empty ? WaveformOverview.Empty : new WaveformOverview(
                new float[] { 0.2f, 0.8f }, 120,
                new float[] { 0.1f, 0.7f }, new float[] { 0.2f, 0.4f }, new float[] { 0.3f, 0.5f });
        }
    }
}
