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

    [Fact]
    public async Task A_superseded_load_failing_does_not_blank_the_newer_waveform()
    {
        // The A -> B -> A case. The first decode is still in flight when the second starts; when it then
        // fails or is torn down, its error handling must not erase what the second already painted.
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        var dispatcher = new FakeDispatcher();
        var provider = new SequencedProvider();
        using var vm = new DeckViewModel(0, dispatcher, provider);

        void Load(string track) => dispatcher.RaiseFeedback(
            PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 128, Argument: track));

        Load("a.flac");                       // first decode starts and hangs
        await provider.WaitForCalls(1);       // the decode runs on Task.Run, so wait for it to arrive
        Load("b.flac");                       // supersedes it
        await provider.WaitForCalls(2);

        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsWaveformLoading) && !vm.IsWaveformLoading)
                settled.TrySetResult();
        };
        provider.Pending[1].SetResult(new WaveformOverview(new float[] { 0.9f, 0.8f }, 200));
        await settled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(vm.Waveform);

        // Now let the abandoned first decode blow up, the way a disposed token source used to make it.
        provider.Pending[0].SetException(new InvalidOperationException("superseded decode failed"));
        await Task.Delay(150);

        Assert.NotNull(vm.Waveform);
        Assert.Equal(2, vm.Waveform!.Count);
    }

    // Hands out a separate completion per call so a test can finish them out of order.
    private sealed class SequencedProvider : IWaveformProvider
    {
        public readonly List<TaskCompletionSource<WaveformOverview>> Pending = new();

        public Task<WaveformOverview> GetOverviewAsync(
            string filePath, int bucketCount, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<WaveformOverview>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Pending)
                Pending.Add(completion);
            return completion.Task;
        }

        /// <summary>Waits until <paramref name="count"/> decodes have actually started.</summary>
        public async Task WaitForCalls(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                lock (Pending)
                    if (Pending.Count >= count)
                        return;
                await Task.Delay(10);
            }

            throw new TimeoutException($"Only {Pending.Count} decode(s) started; expected {count}.");
        }
    }

    private sealed class PendingProvider : IWaveformProvider
    {
        public TaskCompletionSource<WaveformOverview> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<WaveformOverview> GetOverviewAsync(string filePath, int bucketCount, CancellationToken cancellationToken = default)
            => Completion.Task.WaitAsync(cancellationToken);
    }
}
