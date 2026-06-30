using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Persistence;
using Xunit;

namespace Liveolator.Core.Tests.Analysis.Cues;

public class AutoCueServiceTests
{
    private const int Sr = CueTestSignals.SampleRate;
    private const string TrackA = @"C:\Music\a.wav";
    private const string TrackB = @"C:\Music\b.wav";

    private static AutoCueAnalyzer FineGrainedAnalyzer() =>
        new(structuralDetector: new StructuralCueDetector(phraseBars: 2));

    private static AutoCueService Service(
        IAudioDecoder decoder, IHotCueStore store, System.Action<string>? onError = null) =>
        new(decoder, store, analyzer: FineGrainedAnalyzer(), onError: onError);

    [Fact]
    public async Task RunAsync_WritesAutoCuesForTrack()
    {
        var store = new InMemoryHotCueStore();
        var service = Service(new FakeAudioDecoder(CueTestSignals.StructuredClickTrack()), store);

        AutoCueOutcome outcome = await service.RunAsync(new[] { TrackA });

        Assert.Equal(new AutoCueOutcome(1, 1), outcome);
        TrackCueSet saved = store.Records[TrackA].ToCueSet();
        Assert.Contains(saved.HotCues, c => c.Label == "Drop");
        Assert.All(saved.HotCues, c => Assert.True(c.IsAuto));
    }

    [Fact]
    public async Task RunAsync_PreservesManualCue()
    {
        var store = new InMemoryHotCueStore();
        // The DJ committed slot 1 by hand before the auto pass runs.
        var manual = new TrackCueSet(Sr).SetHotCue(1, 123_456, "My Drop", 0x00FF00, isAuto: false);
        store.Records[TrackA] = TrackCueRecord.FromCueSet(TrackA, manual);

        await Service(new FakeAudioDecoder(CueTestSignals.StructuredClickTrack()), store).RunAsync(new[] { TrackA });

        HotCue slot1 = store.Records[TrackA].ToCueSet().GetHotCue(1)!.Value;
        Assert.False(slot1.IsAuto);
        Assert.Equal(123_456, slot1.PositionSamples);
        Assert.Equal("My Drop", slot1.Label);
    }

    [Fact]
    public async Task RunAsync_DecodeFailure_IsIsolated_AndOtherTracksProceed()
    {
        var store = new InMemoryHotCueStore();
        var errors = new List<string>();
        // Track A throws mid-decode; Track B decodes fine.
        var decoder = new FakeAudioDecoder(CueTestSignals.StructuredClickTrack(), throwForPath: TrackA);

        AutoCueOutcome outcome = await Service(decoder, store, onError: errors.Add)
            .RunAsync(new[] { TrackA, TrackB });

        Assert.Equal(2, outcome.Considered);
        Assert.Equal(1, outcome.Cued);              // only B
        Assert.Single(errors);                      // A's failure reported, not swallowed
        Assert.False(store.Records.ContainsKey(TrackA));
        Assert.True(store.Records.ContainsKey(TrackB));
    }

    [Fact]
    public async Task RunAsync_UndetectableTrack_IsNotCued()
    {
        var store = new InMemoryHotCueStore();
        var decoder = new FakeAudioDecoder(new float[Sr]); // silence

        AutoCueOutcome outcome = await Service(decoder, store).RunAsync(new[] { TrackA });

        Assert.Equal(0, outcome.Cued);
        Assert.Empty(store.Records);
    }

    [Fact]
    public async Task RunAsync_Cancellation_Throws()
    {
        var store = new InMemoryHotCueStore();
        var service = Service(new FakeAudioDecoder(CueTestSignals.StructuredClickTrack()), store);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<System.OperationCanceledException>(
            () => service.RunAsync(new[] { TrackA }, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task RunAsync_ReportsProgress()
    {
        var store = new InMemoryHotCueStore();
        var decoder = new FakeAudioDecoder(CueTestSignals.StructuredClickTrack());
        var reports = new List<AutoCueProgress>();
        var progress = new Progress<AutoCueProgress>(reports.Add);

        await Service(decoder, store).RunAsync(new[] { TrackA, TrackB }, progress);

        // Progress is async (posted to the captured context); just assert the final outcome landed.
        Assert.Equal(2, store.Records.Count);
    }

    private sealed class InMemoryHotCueStore : IHotCueStore
    {
        public Dictionary<string, TrackCueRecord> Records { get; } = new();

        public Task<TrackCueRecord?> LoadAsync(string trackPath, CancellationToken cancellationToken = default)
            => Task.FromResult(Records.TryGetValue(trackPath, out TrackCueRecord? r) ? r : null);

        public Task SaveAsync(TrackCueRecord record, CancellationToken cancellationToken = default)
        {
            Records[record.TrackPath] = record;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string trackPath, CancellationToken cancellationToken = default)
        {
            Records.Remove(trackPath);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<string>> ListPathsWithCuesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((IReadOnlyCollection<string>)Records
                .Where(kv => kv.Value.HotCues.Count > 0).Select(kv => kv.Key).ToList());
    }
}
