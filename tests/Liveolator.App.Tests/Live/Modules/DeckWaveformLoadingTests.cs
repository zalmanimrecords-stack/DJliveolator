using System.Reactive.Concurrency;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Waveform;
using ReactiveUI;

namespace Liveolator.App.Tests.Live.Modules;

public sealed class DeckWaveformLoadingTests
{
    [Fact]
    public async Task Loading_indicator_covers_the_decode_wait_and_clears_when_ready()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        var dispatcher = new FakeDispatcher();
        var provider = new PendingProvider();
        using var vm = new DeckViewModel(0, dispatcher, provider);
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 128, Argument: "track.flac"));
        Assert.True(vm.IsWaveformLoading);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.IsWaveformLoading) && !vm.IsWaveformLoading) finished.TrySetResult(); };
        provider.Completion.SetResult(new WaveformOverview(new float[] { 0.5f }, 120));
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(vm.Waveform);
    }

    private sealed class PendingProvider : IWaveformProvider
    {
        public TaskCompletionSource<WaveformOverview> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<WaveformOverview> GetOverviewAsync(string filePath, int bucketCount, CancellationToken cancellationToken = default)
            => Completion.Task.WaitAsync(cancellationToken);
    }
}
