using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Audio;
using Liveolator.Core.Waveform;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Live.Modules;

/// <summary>
/// The kick ticks the DJ PRO comb zips A against B: the analysed on-beat kicks (never the grid), smoothed, as
/// track fractions on the grid's time base — and the same list is what the engine phase-locks on.
/// </summary>
public sealed class DeckKickMarkersTests
{
    private const double Duration = 12.0;

    public DeckKickMarkersTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    // A v12-style analysis whose kick list KickPhaseGate vouches for: 120 BPM, a kick every 0.5 s from 0.25 s,
    // with one 20 ms flam so a raw (unsmoothed) list is distinguishable from the smoothed one.
    private static BpmResult Gated(double firstBeat = 0.25) => new(120.0, 0.9, firstBeat)
    {
        KickOnsetsSeconds = Enumerable.Range(0, 22).Select(i => 0.25 + (i * 0.5) + (i == 10 ? 0.02 : 0.0)).ToArray(),
        KickPhaseMarginRatio = 2.0,
        PhaseWindowDisagreementSeconds = 0.004,
    };

    private static BpmResult Ungated() => Gated() with { KickPhaseMarginRatio = 0.9 };

    [Fact]
    public async Task GatedAnalysis_PublishesTheSmoothedKicks_BeforeTheGridSettles()
    {
        BpmResult analysis = Gated();
        var (vm, dispatcher) = Deck(_ => analysis);

        await LoadAndSettle(vm, dispatcher, @"C:\a.flac");

        double[] expected = FourOnTheFloorKicks.From(analysis).Select(t => t / Duration).ToArray();
        Assert.Equal(expected, vm.KickMarkers);
        Assert.NotEqual(analysis.KickOnsetsSeconds[10] / Duration, vm.KickMarkers[10], 6); // the flam is smoothed away
    }

    [Fact]
    public async Task UngatedAnalysis_PublishesNoKicks()
    {
        var (vm, dispatcher) = Deck(_ => Ungated());

        await LoadAndSettle(vm, dispatcher, @"C:\a.flac");

        Assert.Empty(vm.KickMarkers);
    }

    [Fact]
    public async Task AnalysisWithoutKicks_PublishesNoKicks()
    {
        var (vm, dispatcher) = Deck(_ => Gated() with { KickOnsetsSeconds = Array.Empty<double>() });

        await LoadAndSettle(vm, dispatcher, @"C:\a.flac");

        Assert.Empty(vm.KickMarkers);
    }

    [Fact]
    public async Task NewTrackLoad_ClearsThePreviousKicks_BeforeItsOverviewDecodes()
    {
        var provider = new PerPathWaveformProvider();
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher, provider, trackInfo: null, analysisInfo: _ => Gated());
        await LoadAndSettle(vm, dispatcher, @"C:\a.flac");
        Assert.NotEmpty(vm.KickMarkers);

        RaiseLoad(dispatcher, @"C:\b.flac"); // b's overview never decodes in this test

        Assert.Empty(vm.KickMarkers);
    }

    [Fact]
    public async Task FailedLoad_ClearsTheKicks()
    {
        var (vm, dispatcher) = Deck(_ => Gated());
        await LoadAndSettle(vm, dispatcher, @"C:\a.flac");

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: false, Value: 0, Argument: @"C:\gone.flac"));

        Assert.Empty(vm.KickMarkers);
    }

    [Fact]
    public async Task SetPhase_MovesTheGrid_ButNeverTheKicks()
    {
        var (vm, dispatcher) = Deck(_ => Gated());
        await LoadAndSettle(vm, dispatcher, @"C:\a.flac");
        IReadOnlyList<double> grid = vm.BeatGrid;
        IReadOnlyList<double> kicks = vm.KickMarkers;

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckSetFirstBeat, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0.4));

        Assert.NotEqual(grid, vm.BeatGrid);
        Assert.Same(kicks, vm.KickMarkers);
    }

    [Fact]
    public void CatalogSelfHeal_SendsTheEngineTheOnBeatKicks()
    {
        BpmResult analysis = Gated();
        var (_, dispatcher) = Deck(_ => analysis);

        RaiseLoad(dispatcher, @"C:\a.flac", bpm: 0); // no BPM on the load → the catalog grid is re-emitted

        PerformanceAction firstBeat = Assert.Single(
            dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.DeckSetFirstBeat);
        Assert.Equal(DeckKickOnsetCodec.Encode(FourOnTheFloorKicks.From(analysis)), firstBeat.Argument);
    }

    [Fact]
    public void CatalogSelfHeal_WithoutOnBeatProof_SendsTheEngineNoKicks()
    {
        var (_, dispatcher) = Deck(_ => Ungated());

        RaiseLoad(dispatcher, @"C:\a.flac", bpm: 0);

        PerformanceAction firstBeat = Assert.Single(
            dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.DeckSetFirstBeat);
        Assert.Null(firstBeat.Argument);
    }

    [Fact]
    public void CatalogSelfHeal_SendsTheKicks_EvenWhenTheFirstBeatIsZero()
    {
        BpmResult analysis = Gated(firstBeat: 0.0);
        var (_, dispatcher) = Deck(_ => analysis);

        RaiseLoad(dispatcher, @"C:\a.flac", bpm: 0);

        PerformanceAction firstBeat = Assert.Single(
            dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.DeckSetFirstBeat);
        Assert.NotNull(firstBeat.Argument);
    }

    [Fact]
    public async Task BackgroundAnalysis_PublishesTheKicks_AndSendsThemToTheEngine()
    {
        BpmResult analysis = Gated();
        var result = new TaskCompletionSource<BpmResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(
            slot: 0, dispatcher, FakeWaveformProvider.WithDuration(Duration),
            trackInfo: null, analysisInfo: _ => null, bpmAnalysis: (_, _) => result.Task);

        RaiseLoad(dispatcher, @"C:\a.flac", bpm: 0);
        await WaitUntil(() => !vm.IsWaveformLoading); // the duration is known before the analysis lands
        result.SetResult(analysis);
        await WaitUntil(() => vm.KickMarkers.Count > 0);

        Assert.Equal(FourOnTheFloorKicks.From(analysis).Select(t => t / Duration), vm.KickMarkers);
        await WaitUntil(() => dispatcher.Dispatched.ToArray().Any(a => a.Kind == PerformanceActionKind.DeckSetFirstBeat));
        PerformanceAction firstBeat = dispatcher.Dispatched.ToArray().First(a => a.Kind == PerformanceActionKind.DeckSetFirstBeat);
        Assert.Equal(DeckKickOnsetCodec.Encode(FourOnTheFloorKicks.From(analysis)), firstBeat.Argument);
    }

    [Fact]
    public async Task Sync_WithOnBeatKicks_SnapsTheGridOntoTheNearestKick_ThenLocks()
    {
        var (vm, dispatcher) = Deck(_ => Gated());
        await LoadAndSettle(vm, dispatcher, @"C:\a.flac");
        SeekTo(dispatcher, 0.43);
        dispatcher.Dispatched.Clear();

        await vm.SyncCommand.Execute().ToTask();

        Assert.Equal(
            new[] { PerformanceActionKind.DeckSetFirstBeat, PerformanceActionKind.DeckSyncToggle },
            dispatcher.Dispatched.Select(a => a.Kind));
    }

    [Fact]
    public async Task Sync_WithoutOnBeatKicks_KeepsTheAnalysedAnchor()
    {
        // A pre-v12 row: phase-ready on coherence alone, but no gate numbers, so no kicks. Folding the playhead
        // into a beat instead would hand the engine an arbitrary phase to lock on.
        var (vm, dispatcher) = Deck(_ => Gated() with { KickPhaseMarginRatio = null, PhaseWindowDisagreementSeconds = null });
        await LoadAndSettle(vm, dispatcher, @"C:\a.flac");
        SeekTo(dispatcher, 0.43);
        dispatcher.Dispatched.Clear();

        await vm.SyncCommand.Execute().ToTask();

        PerformanceAction sync = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckSyncToggle, sync.Kind);
    }

    private static void SeekTo(FakeDispatcher dispatcher, double fraction)
        => dispatcher.RaiseFeedback(PerformanceActionKind.DeckSeek, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: fraction));

    private static (DeckViewModel Vm, FakeDispatcher Dispatcher) Deck(Func<string, BpmResult?> analysisInfo)
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(
            slot: 0, dispatcher, FakeWaveformProvider.WithDuration(Duration), trackInfo: null, analysisInfo: analysisInfo);
        return (vm, dispatcher);
    }

    private static void RaiseLoad(FakeDispatcher dispatcher, string path, double bpm = 120.0)
        => dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: bpm, Argument: path));

    // BeatGrid raising is the deck's "load settled" signal, after the overview has decoded.
    private static async Task LoadAndSettle(DeckViewModel vm, FakeDispatcher dispatcher, string path)
    {
        var settled = new TaskCompletionSource();
        void Handler(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeckViewModel.BeatGrid) && vm.BeatGrid.Count > 0)
                settled.TrySetResult();
        }
        vm.PropertyChanged += Handler;
        try
        {
            RaiseLoad(dispatcher, path);
            await settled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            vm.PropertyChanged -= Handler;
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for the deck");
            await Task.Delay(10);
        }
    }

    // a.flac decodes at once; any other path stays decoding, so a test can look at the deck mid-load.
    private sealed class PerPathWaveformProvider : IWaveformProvider
    {
        public Task<WaveformOverview> GetOverviewAsync(
            string filePath, int bucketCount, CancellationToken cancellationToken = default)
            => filePath.EndsWith("a.flac", StringComparison.OrdinalIgnoreCase)
                ? Task.FromResult(new WaveformOverview(new float[8], Duration))
                : Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => WaveformOverview.Empty);
    }
}
