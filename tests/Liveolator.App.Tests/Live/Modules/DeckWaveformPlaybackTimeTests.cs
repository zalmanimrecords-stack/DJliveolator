using System;
using System.ComponentModel;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Live.Modules;

/// <summary>
/// The zoomed deck waveform is drawn in PLAYBACK time, not source time: the visible window scales by the
/// deck's effective playback rate (audible ÷ base tempo). Two decks matched to the same AUDIBLE tempo then
/// show the same wall-clock span, so their kicks share a pixel width + scroll speed and stay stacked while
/// scrolling — the "beat-locked motion" a DJ reads across a synced A/B pair, not just a shared zoom level.
/// </summary>
public sealed class DeckWaveformPlaybackTimeTests
{
    public DeckWaveformPlaybackTimeTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    [Fact]
    public async Task MatchedAudibleTempo_ShowsEqualBeatsPerWindow_DespiteDifferentBaseTempoAndLength()
    {
        // Deck A: base 128, playing at its own tempo (unity rate), a 200 s track.
        var dispA = new FakeDispatcher();
        var vmA = new DeckViewModel(slot: 0, dispA, FakeWaveformProvider.WithDuration(200), waveformZoomSeconds: 8.0);
        await LoadAndSettle(vmA, dispA, slot: 0, baseBpm: 128);

        // Deck B: base 124, SYNCED up to an audible 128 (rate 128/124), a DIFFERENT 250 s track. The engine
        // re-emits the synced follower's audible BPM; its base (recoverable from the min|max range) stays 124.
        var dispB = new FakeDispatcher();
        var vmB = new DeckViewModel(slot: 1, dispB, FakeWaveformProvider.WithDuration(250), waveformZoomSeconds: 8.0);
        await LoadAndSettle(vmB, dispB, slot: 1, baseBpm: 124);
        RaiseBpm(dispB, slot: 1, audibleBpm: 128, baseBpm: 124);

        // Beats visible across the strip = ZoomWindow(fraction) × duration(s) ÷ beatSeconds(at BASE tempo,
        // the tempo the source waveform's kicks actually sit at). Equal audible tempo ⇒ equal beats on
        // screen ⇒ the kicks line up across the whole window, not only at the needle.
        Assert.Equal(BeatsPerWindow(vmA, durationSeconds: 200, baseBpm: 128),
                     BeatsPerWindow(vmB, durationSeconds: 250, baseBpm: 124), precision: 6);
    }

    [Fact]
    public async Task ZoomWindow_ScalesByPlaybackRate_WhenAudibleTempoDivergesFromBase()
    {
        var disp = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, disp, FakeWaveformProvider.WithDuration(250), waveformZoomSeconds: 8.0);
        await LoadAndSettle(vm, disp, slot: 0, baseBpm: 124);

        // Unity rate at load: the source-time window (regression — unchanged from the pre-playback-time math).
        Assert.Equal(8.0 / 250.0, vm.ZoomWindow, 6);

        // Sync/pitch pulls the audible tempo to 128 while the base stays 124 → the window widens in source
        // time by the rate so the wall-clock span (and thus the beat pixel width) is unchanged.
        RaiseBpm(disp, slot: 0, audibleBpm: 128, baseBpm: 124);
        Assert.Equal(8.0 * (128.0 / 124.0) / 250.0, vm.ZoomWindow, 6);
    }

    [Fact]
    public async Task BeatGrid_StaysAtBaseTempo_WhenPitchedAboveBase_SoLinesKeepSittingOnKicks()
    {
        const double duration = 240.0;
        var disp = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, disp, FakeWaveformProvider.WithDuration(duration), waveformZoomSeconds: 8.0);
        await LoadAndSettle(vm, disp, slot: 0, baseBpm: 124);

        // Pitch/sync raises the audible tempo to 128; the source waveform (and its kicks) are unchanged, so
        // the grid must keep the BASE (124) beat spacing — drawing it at the audible tempo would slide the
        // lines off the kicks.
        RaiseBpm(disp, slot: 0, audibleBpm: 128, baseBpm: 124);

        double beatSpacing = vm.BeatGrid[1] - vm.BeatGrid[0];
        Assert.Equal((60.0 / 124.0) / duration, beatSpacing, 6);
    }

    // beats-per-window: source seconds shown (ZoomWindow × duration) divided by one beat at the base tempo.
    private static double BeatsPerWindow(DeckViewModel vm, double durationSeconds, double baseBpm)
        => vm.ZoomWindow * durationSeconds / (60.0 / baseBpm);

    private static async Task LoadAndSettle(DeckViewModel vm, FakeDispatcher disp, int slot, double baseBpm)
    {
        Task gridSet = WaitForProperty(vm, nameof(DeckViewModel.BeatGrid));
        disp.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, slot,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: baseBpm, Argument: @"C:\t.flac"));
        await gridSet;
    }

    // The engine's DeckBpm feedback: Value = audible tempo, Argument = "min|max" pitch range = base×(1∓8%),
    // so the deck recovers the base as (min+max)/2.
    private static void RaiseBpm(FakeDispatcher disp, int slot, double audibleBpm, double baseBpm)
    {
        const double pitchRange = 0.08;
        string range = FormattableString.Invariant($"{baseBpm * (1 - pitchRange)}|{baseBpm * (1 + pitchRange)}");
        disp.RaiseFeedback(PerformanceActionKind.DeckBpm, slot,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: audibleBpm, Argument: range));
    }

    private static Task WaitForProperty(DeckViewModel vm, string propertyName)
    {
        var tcs = new TaskCompletionSource();
        void Handler(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == propertyName)
            {
                vm.PropertyChanged -= Handler;
                tcs.TrySetResult();
            }
        }
        vm.PropertyChanged += Handler;
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
