using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Windows.Input;
using Liveolator.App.Features.Dj;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Beat;
using Liveolator.Core.Playlist;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Dj;

/// <summary>
/// B5 — surfacing played history. As the live queue advances, each track that leaves the Now slot is
/// recorded into a most-recent-first Played list (using the modeled <see cref="TrackState.Played"/>),
/// purely in the DJ view-model — no change to the queue engine or any audio path.
/// </summary>
public sealed class DjViewModelPlayedHistoryTests
{
    public DjViewModelPlayedHistoryTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private sealed class ImmediateBeatScheduler : IBeatScheduler
    {
        public void Schedule(Quantize when, int everyN, Action onFire) => onFire();
    }

    private static LivePlaylist NewPlaylist()
        => new(new ImmediateBeatScheduler(), NullLogger<LivePlaylist>.Instance);

    [Fact]
    public void Played_is_empty_before_any_advance()
    {
        LivePlaylist playlist = NewPlaylist();
        playlist.Load(new[] { "a.mp3", "b.mp3" });

        var vm = new DjViewModel(new FakeDispatcher(), playlist);

        Assert.Empty(vm.Played);
        Assert.True(vm.IsPlayedEmpty);
    }

    [Fact]
    public void Advancing_the_queue_records_the_previous_now_as_played_most_recent_first()
    {
        LivePlaylist playlist = NewPlaylist();
        playlist.Load(new[] { "a.mp3", "b.mp3", "c.mp3" });
        var vm = new DjViewModel(new FakeDispatcher(), playlist);

        playlist.SkipNow(); // a -> b
        playlist.SkipNow(); // b -> c

        Assert.Equal(new[] { "b", "a" }, vm.Played.Select(p => p.Title)); // newest first
        Assert.All(vm.Played, p => Assert.Equal(TrackState.Played, p.State));
        Assert.False(vm.IsPlayedEmpty);
    }

    [Fact]
    public void Played_entries_carry_the_played_label_and_cannot_be_removed()
    {
        LivePlaylist playlist = NewPlaylist();
        playlist.Load(new[] { "a.mp3", "b.mp3" });
        var vm = new DjViewModel(new FakeDispatcher(), playlist);

        playlist.SkipNow(); // a -> b

        SetEntryViewModel played = Assert.Single(vm.Played);
        Assert.Equal("PLAYED", played.StateLabel);
        Assert.False(played.IsNow);
        Assert.False(((ICommand)played.RemoveCommand).CanExecute(null)); // history is read-only
    }

    [Fact]
    public void Reloading_the_set_clears_played_history()
    {
        LivePlaylist playlist = NewPlaylist();
        playlist.Load(new[] { "a.mp3", "b.mp3" });
        var vm = new DjViewModel(new FakeDispatcher(), playlist);
        playlist.SkipNow();
        Assert.NotEmpty(vm.Played);

        playlist.Load(new[] { "x.mp3", "y.mp3" }); // a fresh set

        Assert.Empty(vm.Played);
    }
}
