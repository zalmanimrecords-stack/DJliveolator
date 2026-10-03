using System;
using System.Reactive.Concurrency;
using Liveolator.App.Features.Dj;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Beat;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Playlist;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Dj;

/// <summary>The "PLAYING NEXT" readout on a deck's waveform follows the head of that deck's queue.</summary>
public sealed class DeckUpNextViewModelTests
{
    public DeckUpNextViewModelTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
    }

    private sealed class FireNow : IBeatScheduler
    {
        public void Schedule(Quantize when, int everyN, Action onFire) => onFire();
    }

    private static (LivePlaylist Queue, DeckUpNextViewModel Vm) Build()
    {
        var queue = new LivePlaylist(new FireNow(), NullLogger<LivePlaylist>.Instance);
        var library = new MusicLibrary(new FakeFileEnumerator(), new FakeAudioDecoder());
        return (queue, new DeckUpNextViewModel(queue, library));
    }

    [Fact]
    public void EmptyQueue_ShowsNothing()
    {
        (_, DeckUpNextViewModel vm) = Build();
        Assert.False(vm.HasNext);
    }

    [Fact]
    public void QueuedTrack_IsShownByName_AndClearsWhenRemoved()
    {
        (LivePlaylist queue, DeckUpNextViewModel vm) = Build();
        queue.Load(new[] { "/music/now.mp3", "/music/Waiting Track.mp3" });

        Assert.True(vm.HasNext);
        Assert.Equal("Waiting Track", vm.NextTitle);

        queue.RemoveFuture(queue.Upcoming[0].Id);
        Assert.False(vm.HasNext);
    }
}
