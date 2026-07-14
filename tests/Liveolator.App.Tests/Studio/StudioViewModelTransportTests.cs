using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Studio;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Studio;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Studio;

/// <summary>
/// Transport-state tests for <see cref="StudioViewModel"/>. These guard the transport-bar bindings:
/// the Play button's <c>Classes.on</c> indicator binds to <see cref="StudioViewModel.IsPlaying"/>, and
/// the always-present Play/Stop/Render buttons gate visibility via <c>IsEnabled</c> on
/// <see cref="StudioViewModel.CanPlay"/> / <see cref="StudioViewModel.CanRender"/>.
/// </summary>
public sealed class StudioViewModelTransportTests
{
    public StudioViewModelTransportTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private static MusicLibrary BuildLibrary()
        => new(new FakeFileEnumerator(), new FakeAudioDecoder());

    [Fact]
    public void FreshViewModel_IsNotPlaying()
    {
        var vm = new StudioViewModel(BuildLibrary(), new FakeStore());

        Assert.False(vm.IsPlaying);
    }

    [Fact]
    public void CanPlay_IsFalse_WithoutDispatcherAndClock()
    {
        var vm = new StudioViewModel(BuildLibrary(), new FakeStore());

        Assert.False(vm.CanPlay);
    }

    [Fact]
    public void CanPlay_IsTrue_WhenDispatcherAndClockWired()
    {
        var vm = new StudioViewModel(
            BuildLibrary(), new FakeStore(),
            dispatcher: new RecordingDispatcher(), clock: new StubHostClock());

        Assert.True(vm.CanPlay);
    }

    [Fact]
    public void CanRender_TracksDecoderWiring()
    {
        Assert.False(new StudioViewModel(BuildLibrary(), new FakeStore()).CanRender);
        Assert.True(new StudioViewModel(
            BuildLibrary(), new FakeStore(), decoder: new FakeAudioDecoder()).CanRender);
    }

    private sealed class StubHostClock : IHostClock
    {
        public long TicksPerSecond => 1000;
        public long NowTicks => 0;
    }

    private sealed class RecordingDispatcher : IPerformanceActionDispatcher
    {
        public void Dispatch(PerformanceAction action) { }
        public ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot = 0) => ActionFeedbackState.Unavailable;
        public event EventHandler<ActionFeedbackChanged>? FeedbackChanged { add { } remove { } }
        public event EventHandler<PerformanceAction>? ActionDispatched { add { } remove { } }
    }

    private sealed class FakeStore : IStudioProjectStore
    {
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<StudioProject?> LoadAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<StudioProject?>(null);

        public Task SaveAsync(StudioProject project, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
