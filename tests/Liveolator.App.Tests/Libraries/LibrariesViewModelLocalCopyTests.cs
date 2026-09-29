using System.Reactive.Concurrency;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Library.Music;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Libraries;

public sealed class LibrariesViewModelLocalCopyTests
{
    public LibrariesViewModelLocalCopyTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    [Fact]
    public async Task AdoptLocalCopies_ShowsTheCopies_KeepsTheOriginals_AndRegistersTheFolder()
    {
        var library = new MusicLibrary(new FakeFileEnumerator("/music/a.mp3"), new FakeAudioDecoder());
        await library.ScanAsync(new[] { "/music" });
        using var vm = new LibrariesViewModel(library);
        vm.AddFolder("/music");
        MusicTrack original = library.All.Single();
        MusicTrack copy = original with { File = original.File with { Path = "/gig/music/a.mp3" } };

        vm.AdoptLocalCopies("/gig", new[] { copy });

        Assert.Same(copy, library.TryGet("/gig/music/a.mp3"));
        Assert.NotNull(library.TryGet("/music/a.mp3"));
        Assert.Contains("/gig", vm.Folders);
        Assert.Contains(vm.Tracks, r => r.Track.File.Path == "/gig/music/a.mp3");
    }
}
