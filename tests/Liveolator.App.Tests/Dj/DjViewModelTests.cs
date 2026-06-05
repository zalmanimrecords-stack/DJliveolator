using System;
using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using System.Windows.Input;
using Liveolator.App.Features.Dj;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using Liveolator.Core.Playlist;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Dj;

/// <summary>
/// Verifies the DJ tab view-model: the set mirrors the live Now/Next/Later queue, the decks are the
/// shared modules, and queue edits (skip/remove) go through the dispatcher (doc 04/09).
/// </summary>
public sealed class DjViewModelTests
{
    public DjViewModelTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    /// <summary>A scheduler that fires immediately, so SkipOn(...) advances the queue synchronously in tests.</summary>
    private sealed class ImmediateScheduler2 : IBeatScheduler
    {
        public void Schedule(Quantize when, int everyN, Action onFire) => onFire();
    }

    private static LivePlaylist NewPlaylist()
        => new(new ImmediateScheduler2(), NullLogger<LivePlaylist>.Instance);

    [Fact]
    public void Set_ReflectsPlaylist_NowThenUpcoming()
    {
        LivePlaylist playlist = NewPlaylist();
        playlist.Load(new[] { "a.mp3", "b.mp3", "c.mp3" });

        var vm = new DjViewModel(new FakeDispatcher(), playlist);

        Assert.Equal(3, vm.Set.Count);
        Assert.Equal("a", vm.Set[0].Title);
        Assert.True(vm.Set[0].IsNow);
        Assert.Equal("NOW", vm.Set[0].StateLabel);
        Assert.Equal("NEXT", vm.Set[1].StateLabel);
        Assert.Equal("LATER", vm.Set[2].StateLabel);
        Assert.False(vm.IsSetEmpty);
    }

    [Fact]
    public void Decks_AreTheSharedModules()
    {
        var vm = new DjViewModel(new FakeDispatcher(), NewPlaylist());

        Assert.Equal("A", vm.DeckA.DeckId);
        Assert.Equal("B", vm.DeckB.DeckId);
        Assert.NotNull(vm.Mixer);
    }

    [Fact]
    public async Task Skip_EmitsPlaylistSkipOnNextBar()
    {
        var dispatcher = new FakeDispatcher();
        LivePlaylist playlist = NewPlaylist();
        playlist.Load(new[] { "a.mp3", "b.mp3" });
        var vm = new DjViewModel(dispatcher, playlist);

        await vm.SkipCommand.Execute().ToTask();

        Assert.Contains(dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.PlaylistSkipOnNextBar);
    }

    [Fact]
    public async Task Remove_EmitsPlaylistRemoveFutureTrack_WithEntryId()
    {
        var dispatcher = new FakeDispatcher();
        LivePlaylist playlist = NewPlaylist();
        playlist.Load(new[] { "a.mp3", "b.mp3", "c.mp3" });
        var vm = new DjViewModel(dispatcher, playlist);

        SetEntryViewModel next = vm.Set[1]; // an upcoming entry
        await next.RemoveCommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.PlaylistRemoveFutureTrack, action.Kind);
        Assert.Equal(next.Id.ToString(), action.Argument);
    }

    [Fact]
    public void NowEntry_CannotBeRemoved()
    {
        LivePlaylist playlist = NewPlaylist();
        playlist.Load(new[] { "a.mp3", "b.mp3" });
        var vm = new DjViewModel(new FakeDispatcher(), playlist);

        SetEntryViewModel now = vm.Set[0];
        Assert.True(now.IsNow);
        Assert.False(((ICommand)now.RemoveCommand).CanExecute(null));
    }

    [Fact]
    public async Task Skip_AdvancesNow_ThroughTheDispatcher()
    {
        LivePlaylist playlist = NewPlaylist();
        playlist.Load(new[] { "a.mp3", "b.mp3", "c.mp3" });
        var dispatcher = new PerformanceActionDispatcher(
            new IPerformanceActionHandler[] { new PlaylistActionHandler(playlist, NullLogger<PlaylistActionHandler>.Instance) },
            NullLogger<PerformanceActionDispatcher>.Instance);
        var vm = new DjViewModel(dispatcher, playlist);

        Assert.Equal("a", vm.Set[0].Title);

        await vm.SkipCommand.Execute().ToTask();

        Assert.Equal("b", vm.Set[0].Title);
        Assert.True(vm.Set[0].IsNow);
    }

    [Fact]
    public void EmptyPlaylist_ReportsEmptySet()
    {
        var vm = new DjViewModel(new FakeDispatcher(), NewPlaylist());
        Assert.True(vm.IsSetEmpty);
        Assert.Empty(vm.Set);
    }

    [Fact]
    public void NoServices_DisablesSetControls()
    {
        var vm = new DjViewModel();
        Assert.False(vm.IsEnabled);
        Assert.False(vm.HasLibrary);
    }
}
